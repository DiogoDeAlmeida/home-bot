namespace HomelabHub.Modules.Media.Correlation;

/// <summary>
/// Projection destinée à l'affichage : déjà triée, déjà bornée, sans présentation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le module décide de ce qui est intéressant ; les adaptateurs décident seulement de son
/// apparence.</b> Si chaque adaptateur tronquait lui-même la file, Discord et le web
/// afficheraient des sélections différentes du même instant, et personne ne s'en apercevrait
/// avant de les comparer côte à côte.
/// </para>
/// <para>
/// Les données restent brutes — octets, secondes, énumérations — conformément à ADR-0006 : il
/// n'y a pas de modèle de rendu partagé, chaque adaptateur formate à sa façon.
/// </para>
/// </remarks>
/// <param name="Top">Parcours les plus dignes d'attention, déjà classés et bornés.</param>
/// <param name="TotalJourneys">Nombre total de parcours connus.</param>
/// <param name="Downloading">Parcours dont au moins un téléchargement est en cours.</param>
/// <param name="Importing">Parcours en attente ou en cours d'import.</param>
/// <param name="NeedsAttention">Parcours demandant une intervention.</param>
/// <param name="DownloadSpeed">Débit cumulé, en octets par seconde.</param>
/// <param name="BytesRemaining">Octets restants, agrégés après regroupement par téléchargement.</param>
/// <param name="BytesTotal">Octets totaux des téléchargements connus.</param>
/// <param name="ObservedAt">Instant du cycle. <c>null</c> tant qu'aucun n'a eu lieu.</param>
/// <param name="UnavailableSources">
/// Services n'ayant pas répondu. Affiché explicitement : une liste vide parce qu'un service est
/// éteint ne doit pas se lire comme « rien ne télécharge ».
/// </param>
public sealed record MediaOverview(
    IReadOnlyList<JourneySummary> Top,
    int TotalJourneys,
    int Downloading,
    int Importing,
    int NeedsAttention,
    long DownloadSpeed,
    long BytesRemaining,
    long BytesTotal,
    DateTimeOffset? ObservedAt,
    IReadOnlyList<string> UnavailableSources)
{
    /// <summary>Taille du palmarès. Cinq, comme convenu au cadrage pour le message permanent.</summary>
    public const int TopCount = 5;

    public static MediaOverview From(MediaSnapshot snapshot, int take = TopCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new MediaOverview(
            Top: [.. snapshot.MostInteresting(take).Select(JourneySummary.From)],
            TotalJourneys: snapshot.Journeys.Count,
            Downloading: snapshot.Journeys.Count(j => j.State == JourneyState.Downloading),
            Importing: snapshot.Journeys.Count(j => j.State == JourneyState.Importing),
            NeedsAttention: snapshot.AttentionCount,
            DownloadSpeed: snapshot.Journeys.Sum(j => j.DownloadSpeed),
            BytesRemaining: snapshot.BytesRemaining,
            BytesTotal: snapshot.BytesTotal,
            ObservedAt: snapshot.ObservedAt,
            UnavailableSources: snapshot.UnavailableSources);
    }
}

/// <summary>Un parcours aplati pour l'affichage.</summary>
/// <param name="Key">Identité stable, utilisable dans un identifiant de contrôle.</param>
/// <param name="Title">Titre affichable.</param>
/// <param name="MediaType">Film ou série.</param>
/// <param name="State">État global.</param>
/// <param name="NeedsAttention">Ce parcours demande une intervention.</param>
/// <param name="Progress">Progression de 0 à 1, agrégée sur les octets.</param>
/// <param name="DownloadSpeed">Débit cumulé.</param>
/// <param name="BytesRemaining">Octets restants.</param>
/// <param name="EstimatedTimeLeft">
/// Durée restante la plus longue parmi les téléchargements — c'est elle qui détermine quand le
/// média sera réellement disponible, pas la plus courte.
/// </param>
/// <param name="DownloadCount">Nombre de téléchargements distincts, jamais d'entrées de file.</param>
/// <param name="EpisodeCount">Épisodes couverts. Zéro pour un film.</param>
/// <param name="RequestedAt">Date de la demande Seerr, ou <c>null</c> pour un import manuel.</param>
public sealed record JourneySummary(
    string Key,
    string? Title,
    MediaKind MediaType,
    JourneyState State,
    bool NeedsAttention,
    double Progress,
    long DownloadSpeed,
    long BytesRemaining,
    TimeSpan? EstimatedTimeLeft,
    int DownloadCount,
    int EpisodeCount,
    DateTimeOffset? RequestedAt)
{
    public static JourneySummary From(MediaJourney journey)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var etas = journey.Downloads
            .Select(d => d.Torrent?.EstimatedTimeLeft)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToList();

        return new JourneySummary(
            Key: journey.Key,
            Title: journey.Title,
            MediaType: journey.MediaType,
            State: journey.State,
            NeedsAttention: journey.NeedsAttention,
            Progress: journey.Progress,
            DownloadSpeed: journey.DownloadSpeed,
            BytesRemaining: journey.Downloads.Sum(d => d.SizeLeft),
            // Le maximum, pas le minimum : un média n'est disponible que lorsque son dernier
            // téléchargement est fini.
            EstimatedTimeLeft: etas.Count > 0 ? etas.Max() : null,
            DownloadCount: journey.Downloads.Count,
            EpisodeCount: journey.Downloads.Sum(d => d.Episodes.Count),
            RequestedAt: journey.Request?.RequestedAt);
    }
}
