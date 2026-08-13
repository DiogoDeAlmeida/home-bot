#!/usr/bin/env bash
# Mise à jour manuelle de Homelab Hub, avec rollback binaire + base corrélés.
#
# Toujours un geste explicite (ADR-0019) : ce hub détient les clés de tout le homelab, aucune
# mise à jour ne s'applique d'elle-même. La vérification périodique de nouvelle version
# (SystemPoller) ne fait que signaler dans Discord — c'est ce script qu'il faut lancer, à la
# main, pour agir dessus.
#
#   sudo deploy/update.sh              # met à jour vers la dernière version publiée
#   sudo deploy/update.sh v0.3.0       # cible une version précise
#   sudo deploy/update.sh --rollback   # revient sur la dernière mise à jour, hors ligne de commande
#
# Le point de départ de ce script est un trou identifié en revue avant qu'il ne soit écrit :
# un rollback qui se contente de reposer l'ancien binaire, sans coordonner l'état de la base,
# peut remplacer un binaire cassé par un binaire qui plante contre sa propre base — pire que le
# point de départ. Ici, chaque tentative de mise à jour prend elle-même une sauvegarde
# nommément associée à cette tentative (pre-update-<de>-vers-<à>-<horodatage>.db) AVANT de
# toucher au binaire, et c'est cette sauvegarde précise — jamais « la plus récente » au sens
# large — que le rollback restaure.
set -euo pipefail

REPO="DiogoDeAlmeida/home-bot"
INSTALL_ROOT="/opt/homelabhub"
DATA_DIR="${INSTALL_ROOT}/data"
BACKUPS_DIR="${DATA_DIR}/backups"
RELEASES_DIR="${INSTALL_ROOT}/releases"
CURRENT_LINK="${INSTALL_ROOT}/current"
SERVICE_NAME="homelabhub"
DB_FILE="${DATA_DIR}/homelabhub.db"
KEEP_RELEASES=3
KEEP_BACKUPS=5
HEALTHZ_URL="http://127.0.0.1:8080/healthz"

msg_info()  { printf '\033[36m→ %s\033[0m\n' "$1"; }
msg_ok()    { printf '\033[32m✓ %s\033[0m\n' "$1"; }
msg_error() { printf '\033[31m✗ %s\033[0m\n' "$1" >&2; }

if [[ $EUID -ne 0 ]]; then
  msg_error "Ce script doit être lancé en root (sudo)."
  exit 1
fi

if [[ ! -L "$CURRENT_LINK" ]]; then
  msg_error "${CURRENT_LINK} introuvable — utilisez deploy/install.sh pour une première installation."
  exit 1
fi

CURRENT_TAG=$(basename "$(readlink -f "$CURRENT_LINK")")

# Auto-réparation : un LXC installé avant que ce lien n'existe (trouvé en conditions réelles —
# « update: command not found », la toute première fois que ce script était appelé comme un
# utilisateur l'aurait fait plutôt que par son chemin complet) le récupère ici, avant même de
# savoir si la mise à jour elle-même va réussir. Idempotent, donc sans effet une fois en place.
ln -sf "${INSTALL_ROOT}/update.sh" /usr/local/bin/homelabhub-update

# ── Attente sur /healthz, tolérante à la poignée de main Discord ──────────────────────
# Voir Program.cs (ADR-0019) : juste après un démarrage, l'état « Connecting » est normal le
# temps que la passerelle Discord s'établisse, et /healthz le rapporte comme dégradé. 90 s à
# raison d'un essai toutes les 3 s absorbe cette fenêtre sans la masquer indéfiniment : un hub
# qui ne quitte jamais cet état est un vrai échec, pas un faux positif de démarrage.
wait_for_healthy() {
  for _ in $(seq 1 30); do
    if curl -fsS "$HEALTHZ_URL" >/dev/null 2>&1; then
      return 0
    fi
    sleep 3
  done
  return 1
}

restore_database() {
  local backup_file="$1"
  msg_info "Restauration de la base depuis $(basename "$backup_file")."
  rm -f "${DB_FILE}-wal" "${DB_FILE}-shm"
  cp "$backup_file" "$DB_FILE"
}

rollback_to() {
  local previous_tag="$1"
  local backup_file="${2:-}"

  msg_error "Rollback vers ${previous_tag}."
  systemctl stop "$SERVICE_NAME"

  if [[ -n "$backup_file" ]]; then
    restore_database "$backup_file"
  fi

  ln -sfn "${RELEASES_DIR}/${previous_tag}" "$CURRENT_LINK"
  systemctl start "$SERVICE_NAME"

  if wait_for_healthy; then
    msg_ok "Rollback vers ${previous_tag} terminé, /healthz sain."
  else
    msg_error "Le rollback lui-même ne répond pas sainement sur /healthz."
    echo "Intervention manuelle requise : journalctl -u ${SERVICE_NAME} -e" >&2
  fi
}

# ── Mode rollback manuel, hors mise à jour en cours ────────────────────────────────────
if [[ "${1:-}" == "--rollback" ]]; then
  previous=$(find "$RELEASES_DIR" -maxdepth 1 -mindepth 1 -type d ! -name "$CURRENT_TAG" \
    -printf '%T@ %f\n' | sort -rn | head -n1 | cut -d' ' -f2)
  if [[ -z "$previous" ]]; then
    msg_error "Aucune version précédente trouvée sous ${RELEASES_DIR}."
    exit 1
  fi

  latest_backup=$(find "$BACKUPS_DIR" -maxdepth 1 -name 'pre-update-*.db' -printf '%T@ %p\n' \
    2>/dev/null | sort -rn | head -n1 | cut -d' ' -f2- || true)

  rollback_to "$previous" "$latest_backup"
  exit 0
fi

# ── Résolution de la version cible ─────────────────────────────────────────────────────
TARGET_TAG="${1:-latest}"
if [[ "$TARGET_TAG" == "latest" ]]; then
  TARGET_TAG=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | jq -r .tag_name)
fi

if [[ -z "$TARGET_TAG" || "$TARGET_TAG" == "null" ]]; then
  msg_error "Impossible de déterminer la version cible."
  exit 1
fi

if [[ "$TARGET_TAG" == "$CURRENT_TAG" ]]; then
  msg_ok "Déjà en ${CURRENT_TAG} — rien à faire."
  exit 0
fi

msg_info "Mise à jour ${CURRENT_TAG} → ${TARGET_TAG}."

# ── Téléchargement et vérification, service courant intact pendant ce temps ───────────
ARCHIVE="homelabhub-linux-x64-${TARGET_TAG}.tar.gz"
WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT

BASE_URL="https://github.com/${REPO}/releases/download/${TARGET_TAG}"
curl -fsSL -o "${WORK_DIR}/${ARCHIVE}" "${BASE_URL}/${ARCHIVE}"
curl -fsSL -o "${WORK_DIR}/${ARCHIVE}.sha256" "${BASE_URL}/${ARCHIVE}.sha256"
(cd "$WORK_DIR" && sha256sum -c "${ARCHIVE}.sha256") \
  || { msg_error "Somme de contrôle invalide pour ${ARCHIVE} — mise à jour annulée, rien de touché."; exit 1; }

RELEASE_DIR="${RELEASES_DIR}/${TARGET_TAG}"
mkdir -p "$RELEASE_DIR"
tar xzf "${WORK_DIR}/${ARCHIVE}" -C "$RELEASE_DIR"
chmod +x "${RELEASE_DIR}/homelabhub"
chown -R "root:homelabhub" "$RELEASE_DIR"
chmod -R 750 "$RELEASE_DIR"

missing=$(ldd "${RELEASE_DIR}/homelabhub" 2>/dev/null | grep "not found" || true)
if [[ -n "$missing" ]]; then
  msg_error "Bibliothèque(s) manquante(s) pour ${TARGET_TAG} — mise à jour annulée, rien de touché."
  echo "$missing" >&2
  rm -rf "$RELEASE_DIR"
  exit 1
fi
msg_ok "Archive ${TARGET_TAG} téléchargée, vérifiée, en place."

# L'unité systemd est rafraîchie ici, avant l'arrêt — pas seulement à l'installation initiale.
# Une version qui compte sur une directive nouvelle (PrivateTmp=true en a été un exemple réel,
# ADR-0019) doit la trouver déjà en place au moment où elle démarre, pas après. daemon-reload est
# sans effet si le fichier n'a pas changé.
curl -fsSL -o /etc/systemd/system/homelabhub.service \
  "https://raw.githubusercontent.com/${REPO}/${TARGET_TAG}/deploy/systemd/homelabhub.service"
systemctl daemon-reload

# ── Sauvegarde corrélée à cette tentative précise, avant tout arrêt de service ─────────
TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
BACKUP_FILE="${BACKUPS_DIR}/pre-update-${CURRENT_TAG}-to-${TARGET_TAG}-${TIMESTAMP}.db"
msg_info "Sauvegarde de la base avant bascule."
sqlite3 "$DB_FILE" "VACUUM INTO '${BACKUP_FILE}'"
chown homelabhub:homelabhub "$BACKUP_FILE"
msg_ok "Sauvegarde : $(basename "$BACKUP_FILE")."

# ── Bascule ──────────────────────────────────────────────────────────────────────────
msg_info "Arrêt du service."
systemctl stop "$SERVICE_NAME"

ln -sfn "$RELEASE_DIR" "$CURRENT_LINK"

msg_info "Démarrage sur ${TARGET_TAG}."
systemctl start "$SERVICE_NAME"

if wait_for_healthy; then
  msg_ok "Mise à jour vers ${TARGET_TAG} terminée, /healthz sain."

  # Se réinstalle lui-même à la version qui correspond à ce qui vient d'être déployé — un futur
  # `sudo /opt/homelabhub/update.sh` doit refléter le rollback ou les correctifs livrés avec
  # cette version-là, pas rester figé sur la copie de l'installation initiale.
  curl -fsSL -o "${INSTALL_ROOT}/update.sh" \
    "https://raw.githubusercontent.com/${REPO}/${TARGET_TAG}/deploy/update.sh"
  chmod +x "${INSTALL_ROOT}/update.sh"

  # Rétention : conserve les KEEP_RELEASES dernières versions et KEEP_BACKUPS dernières
  # sauvegardes de mise à jour — assez pour un rollback manuel a posteriori (--rollback),
  # sans laisser le disque grossir indéfiniment à chaque mise à jour.
  find "$RELEASES_DIR" -maxdepth 1 -mindepth 1 -type d -printf '%T@ %p\n' \
    | sort -rn | tail -n +$((KEEP_RELEASES + 1)) | cut -d' ' -f2- | xargs -r rm -rf
  find "$BACKUPS_DIR" -maxdepth 1 -name 'pre-update-*.db' -printf '%T@ %p\n' \
    | sort -rn | tail -n +$((KEEP_BACKUPS + 1)) | cut -d' ' -f2- | xargs -r rm -f
else
  rollback_to "$CURRENT_TAG" "$BACKUP_FILE"
  exit 1
fi
