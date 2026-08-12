#!/usr/bin/env bash
# Installation initiale de Homelab Hub sur un LXC Debian dédié.
#
# À exécuter en root, dans un conteneur créé pour cet usage (cadrage §2 : LXC Debian 13 dédié) :
#
#   curl -fsSL https://raw.githubusercontent.com/DiogoDeAlmeida/home-bot/main/deploy/install.sh | bash
#
# Ne teste jamais sur un LXC de production sans être passé par un LXC jetable d'abord — cinq
# bugs trouvés uniquement en conditions réelles sur les tranches précédentes, voir
# CONTRIBUTING.md et docs/03-deploiement.md.
set -euo pipefail

REPO="DiogoDeAlmeida/home-bot"
INSTALL_ROOT="/opt/homelabhub"
DATA_DIR="${INSTALL_ROOT}/data"
RELEASES_DIR="${INSTALL_ROOT}/releases"
CURRENT_LINK="${INSTALL_ROOT}/current"
CONFIG_DIR="/etc/homelabhub"
SERVICE_USER="homelabhub"
SERVICE_NAME="homelabhub"
TARGET_TAG="${1:-latest}"

msg_info()  { printf '\033[36m→ %s\033[0m\n' "$1"; }
msg_ok()    { printf '\033[32m✓ %s\033[0m\n' "$1"; }
msg_error() { printf '\033[31m✗ %s\033[0m\n' "$1" >&2; }

if [[ $EUID -ne 0 ]]; then
  msg_error "Ce script doit être lancé en root (sudo)."
  exit 1
fi

if [[ -e "$CURRENT_LINK" ]]; then
  msg_error "${CURRENT_LINK} existe déjà — une installation est déjà en place. Utilisez deploy/update.sh."
  exit 1
fi

# ── Dépendances : vérifiées, jamais supposées présentes ──────────────────────────────────
msg_info "Vérification des dépendances."
apt-get update -qq
# curl : téléchargement des releases. tar : extraction. jq : lecture fiable de l'API GitHub,
# préférée à un grep/sed fragile sur du JSON. sqlite3 : sauvegarde corrélée par deploy/update.sh
# (VACUUM INTO), vérifié ici aussi pour que l'installation et la mise à jour partagent les mêmes
# prérequis. libicu-dev : ADR-0001, le hub tourne en InvariantGlobalization=false pour un
# formatage français correct des dates et nombres — sans la bibliothèque native, le binaire
# démarre puis plante à la première mise en forme.
for pkg in curl ca-certificates tar jq sqlite3 libicu-dev; do
  if ! dpkg -s "$pkg" >/dev/null 2>&1; then
    msg_info "Installation de ${pkg}."
    apt-get install -y -qq "$pkg"
  fi
done
msg_ok "Dépendances présentes."

# ── Utilisateur système dédié ──────────────────────────────────────────────────────────
if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  msg_info "Création de l'utilisateur système ${SERVICE_USER}."
  useradd --system --no-create-home --home-dir "$INSTALL_ROOT" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# ── Arborescence ────────────────────────────────────────────────────────────────────────
mkdir -p "$RELEASES_DIR" "$DATA_DIR" "$DATA_DIR/backups" "$CONFIG_DIR"
chown -R "${SERVICE_USER}:${SERVICE_USER}" "$DATA_DIR" "$CONFIG_DIR"
chmod 750 "$DATA_DIR" "$CONFIG_DIR"

# ── Résolution de la version cible ─────────────────────────────────────────────────────
if [[ "$TARGET_TAG" == "latest" ]]; then
  msg_info "Résolution de la dernière version publiée."
  TARGET_TAG=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | jq -r .tag_name)
fi

if [[ -z "$TARGET_TAG" || "$TARGET_TAG" == "null" ]]; then
  msg_error "Impossible de déterminer la version à installer."
  exit 1
fi

msg_info "Installation de ${TARGET_TAG}."

# ── Téléchargement et vérification ─────────────────────────────────────────────────────
ARCHIVE="homelabhub-linux-x64-${TARGET_TAG}.tar.gz"
WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT

BASE_URL="https://github.com/${REPO}/releases/download/${TARGET_TAG}"
curl -fsSL -o "${WORK_DIR}/${ARCHIVE}" "${BASE_URL}/${ARCHIVE}"
curl -fsSL -o "${WORK_DIR}/${ARCHIVE}.sha256" "${BASE_URL}/${ARCHIVE}.sha256"

(cd "$WORK_DIR" && sha256sum -c "${ARCHIVE}.sha256") \
  || { msg_error "Somme de contrôle invalide pour ${ARCHIVE}."; exit 1; }
msg_ok "Archive téléchargée et vérifiée."

RELEASE_DIR="${RELEASES_DIR}/${TARGET_TAG}"
mkdir -p "$RELEASE_DIR"
tar xzf "${WORK_DIR}/${ARCHIVE}" -C "$RELEASE_DIR"
chmod +x "${RELEASE_DIR}/homelabhub"
chown -R "root:${SERVICE_USER}" "$RELEASE_DIR"
chmod -R 750 "$RELEASE_DIR"

# ── Dépendances natives du binaire lui-même, vérifiées après coup ─────────────────────
missing=$(ldd "${RELEASE_DIR}/homelabhub" 2>/dev/null | grep "not found" || true)
if [[ -n "$missing" ]]; then
  msg_error "Bibliothèque(s) manquante(s) pour ce binaire :"
  echo "$missing" >&2
  exit 1
fi

ln -s "$RELEASE_DIR" "$CURRENT_LINK"
msg_ok "Version ${TARGET_TAG} en place."

# ── Service systemd ─────────────────────────────────────────────────────────────────────
msg_info "Installation de l'unité systemd."
curl -fsSL -o /etc/systemd/system/homelabhub.service \
  "https://raw.githubusercontent.com/${REPO}/${TARGET_TAG}/deploy/systemd/homelabhub.service"
systemctl daemon-reload
systemctl enable --now "$SERVICE_NAME"

# deploy/update.sh n'est pas dans l'archive de release (c'est un outil d'exploitation, pas un
# artefact publié) : installé ici, à côté du binaire, à la version qui correspond à ce qui vient
# d'être installé. update.sh se réinstalle lui-même à chaque mise à jour, pour la même raison.
curl -fsSL -o "${INSTALL_ROOT}/update.sh" \
  "https://raw.githubusercontent.com/${REPO}/${TARGET_TAG}/deploy/update.sh"
chmod +x "${INSTALL_ROOT}/update.sh"

# ── Vérification du premier démarrage ──────────────────────────────────────────────────
# Fenêtre de tolérance : juste après un démarrage, la passerelle Discord passe par l'état
# « Connecting » le temps de sa poignée de main, ce que /healthz rapporte comme dégradé
# (Program.cs, ADR-0019). Sans configuration Discord (jeton/serveur absents), la sonde est
# saine dès l'ouverture du port.
msg_info "Attente du premier /healthz sain (jusqu'à 90 s)."
healthy=false
for _ in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:8080/healthz" >/dev/null 2>&1; then
    healthy=true
    break
  fi
  sleep 3
done

if [[ "$healthy" == true ]]; then
  msg_ok "Homelab Hub ${TARGET_TAG} installé et opérationnel."
  echo "Configuration à compléter depuis l'interface web : http://<IP-du-LXC>:8080"
else
  msg_error "Le service ne répond pas sainement sur /healthz après 90 s."
  echo "Diagnostic : journalctl -u ${SERVICE_NAME} -e" >&2
  exit 1
fi
