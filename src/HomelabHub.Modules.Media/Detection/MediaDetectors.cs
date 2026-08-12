using System.Globalization;
using HomelabHub.Abstractions.Events;
using HomelabHub.Modules.Media.Correlation;

namespace HomelabHub.Modules.Media.Detection;

/// <summary>Seuils de détection, tous configurables.</summary>
/// <param name="StalledAfter">Inactivité au-delà de laquelle un téléchargement est dit bloqué.</param>
/// <param name="GraceAfterAdded">
/// Délai pendant lequel un téléchargement fraîchement ajouté et n'ayant jamais progressé est
/// laissé tranquille.
/// </param>
public readonly record struct DetectionThresholds(TimeSpan StalledAfter, TimeSpan GraceAfterAdded);

/// <summary>
/// Traduit un snapshot en anomalies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Projection sans état</b> (ADR-0005). À chaque cycle, les détecteurs republient l'ensemble
/// de ce qui va mal, en repartant du snapshot. Ils ne mémorisent rien et ne ferment jamais
/// explicitement une anomalie : ce qui cesse d'être republié est résolu par le noyau.
/// </para>
/// <para>
/// <b>Fonction pure</b>, comme le corrélateur : elle prend un snapshot et rend des événements.
/// C'est ce qui permet de tester chaque seuil sans horloge ni réseau.
/// </para>
/// </remarks>
public static class MediaDetectors
{
    public static IReadOnlyList<HubEvent> Detect(
        MediaSnapshot snapshot,
        DetectionThresholds thresholds,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var events = new List<HubEvent>();

        foreach (var journey in snapshot.Journeys)
        {
            foreach (var download in journey.Downloads)
            {
                AddStalled(events, journey, download, thresholds, now);

                // Précédence entre détecteurs. Un import bloqué a toujours une santé dégradée :
                // les deux détecteurs se déclencheraient sur le même téléchargement, et
                // l'utilisateur recevrait « Manual Import required » puis « le service rapporte
                // un état Warning », qui n'ajoute rien. Constaté en conditions réelles.
                //
                // Le détecteur générique ne parle donc que lorsqu'aucun détecteur spécifique
                // n'a couvert le cas — c'est son rôle : dire qu'il se passe quelque chose qu'on
                // ne sait pas encore nommer.
                if (!AddImportPending(events, journey, download, now))
                {
                    AddUnhealthy(events, journey, download, now);
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Téléchargement sans activité depuis trop longtemps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// L'inactivité est lue chez qBittorrent — <c>last_activity</c> — et non mesurée par le hub
    /// entre deux cycles. C'est ce qui permet à la détection de rester un état dérivé
    /// (ADR-0015) : après un redémarrage, le premier cycle sait déjà depuis combien de temps un
    /// torrent dort.
    /// </para>
    /// <para>
    /// La double condition évite le faux positif de la première seconde : un torrent
    /// fraîchement récupéré n'a pas encore trouvé de pair et se déclare « stalled » alors que
    /// tout est normal. On n'alerte donc que s'il a <b>déjà progressé une fois</b>, ou s'il
    /// dépasse le délai de grâce depuis son ajout.
    /// </para>
    /// </remarks>
    private static void AddStalled(
        List<HubEvent> events,
        MediaJourney journey,
        DownloadItem download,
        DetectionThresholds thresholds,
        DateTimeOffset now)
    {
        // Sans torrent, l'inactivité n'est pas observable : on ne devine pas.
        if (download.Torrent is not { } torrent || download.State != DownloadState.Downloading)
        {
            return;
        }

        var idleFor = IdleFor(torrent, now);
        if (idleFor is null || idleFor < thresholds.StalledAfter)
        {
            return;
        }

        var age = download.AddedAt is { } added ? now - added : TimeSpan.MaxValue;
        if (!download.HasProgressed && age < thresholds.GraceAfterAdded)
        {
            return;
        }

        events.Add(new HubEvent(
            ModuleKey: "media",
            Type: "media.download.stalled",
            Severity: HubEventSeverity.Warning,
            Title: $"Téléchargement bloqué : {journey.Title ?? journey.Key}",
            Body: string.Create(CultureInfo.GetCultureInfo("fr-FR"),
                $"Aucune activité depuis {idleFor.Value.TotalMinutes:0} minutes, "
                + $"à {download.Progress:P0} — {torrent.NumSeeds} source(s) connectée(s)."),
            DedupeKey: $"media.download.stalled:{download.DownloadId}",
            Data: new Dictionary<string, string>
            {
                ["downloadId"] = download.DownloadId,
                ["journey"] = journey.Key,
                ["idleMinutes"] = idleFor.Value.TotalMinutes.ToString("0", CultureInfo.InvariantCulture),
            },
            OccurredAt: now));
    }

    /// <summary>
    /// Import en attente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aucun seuil de durée, et ce n'est pas un oubli.</b> Dans le cas nominal, cette fenêtre
    /// dure moins de cinq secondes — mesuré en capture — donc bien moins que l'intervalle de
    /// polling. Le voir au cycle signifie qu'il persiste : l'échantillonnage applique déjà le
    /// seuil, gratuitement. En ajouter un explicite reviendrait à l'appliquer deux fois et à
    /// retarder la détection sans rien gagner (ADR-0015).
    /// </para>
    /// <para>
    /// Le corps reprend <c>statusMessages</c> <b>mot pour mot</b>. Sur le cas réel observé, ce
    /// champ portait la seule explication existante — « release was matched to movie by ID.
    /// Manual Import required. » — introuvable ailleurs, ni dans l'historique ni dans les
    /// journaux. On le restitue sans jamais l'analyser : c'est une phrase du service, pas un
    /// code d'erreur.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> si ce téléchargement est couvert, pour que le détecteur générique se taise.</returns>
    private static bool AddImportPending(
        List<HubEvent> events, MediaJourney journey, DownloadItem download, DateTimeOffset now)
    {
        if (download.State != DownloadState.Importing)
        {
            return false;
        }

        var explanation = download.StatusMessages.Count > 0
            ? string.Join(" ", download.StatusMessages)
            : "Le téléchargement est terminé mais l'import n'a pas abouti.";

        events.Add(new HubEvent(
            ModuleKey: "media",
            Type: "media.import.pending",
            Severity: HubEventSeverity.Warning,
            Title: $"Import en attente : {journey.Title ?? journey.Key}",
            Body: explanation,
            DedupeKey: $"media.import.pending:{download.DownloadId}",
            Data: new Dictionary<string, string>
            {
                ["downloadId"] = download.DownloadId,
                ["journey"] = journey.Key,
            },
            OccurredAt: now));

        return true;
    }

    /// <summary>
    /// Santé rapportée dégradée, sans cause déjà nommée.
    /// </summary>
    /// <remarks>
    /// Signalé immédiatement, mais <b>toujours en avertissement, jamais en erreur</b> : tant que
    /// <c>statusMessages</c> n'a pas été observé sur un cas réellement bloqué, on ne sait pas
    /// interpréter la gravité. Un signal dont on ignore la sémantique vaut mieux inutilisé que
    /// mal utilisé (ADR-0015).
    /// </remarks>
    private static void AddUnhealthy(
        List<HubEvent> events, MediaJourney journey, DownloadItem download, DateTimeOffset now)
    {
        if (download.Health is not (DownloadHealth.Warning or DownloadHealth.Error))
        {
            return;
        }

        events.Add(new HubEvent(
            ModuleKey: "media",
            Type: "media.download.unhealthy",
            Severity: HubEventSeverity.Warning,
            Title: $"Téléchargement signalé : {journey.Title ?? journey.Key}",
            Body: $"Le service rapporte un état « {download.Health} » sur ce téléchargement.",
            DedupeKey: $"media.download.unhealthy:{download.DownloadId}",
            Data: new Dictionary<string, string>
            {
                ["downloadId"] = download.DownloadId,
                ["journey"] = journey.Key,
                ["health"] = download.Health.ToString(),
            },
            OccurredAt: now));
    }

    /// <summary>Durée depuis la dernière activité du torrent, telle que qBittorrent la rapporte.</summary>
    private static TimeSpan? IdleFor(Contracts.QBittorrentTorrent torrent, DateTimeOffset now)
    {
        if (torrent.LastActivity <= 0)
        {
            return null;
        }

        var last = DateTimeOffset.FromUnixTimeSeconds(torrent.LastActivity);
        var idle = now - last;

        // Une horloge décalée entre le hub et le LXC peut produire un écart négatif : on ne
        // signale pas un blocage sur la foi d'une soustraction absurde.
        return idle < TimeSpan.Zero ? null : idle;
    }
}
