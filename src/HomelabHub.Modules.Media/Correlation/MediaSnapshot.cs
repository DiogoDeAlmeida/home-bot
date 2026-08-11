using HomelabHub.Modules.Media.Contracts;

namespace HomelabHub.Modules.Media.Correlation;

/// <summary>
/// Vue agrégée du domaine média, reconstruite intégralement à chaque cycle.
/// </summary>
/// <remarks>
/// <b>État dérivé, jamais possédé</b> (ADR-0015) : aucune information n'existe uniquement ici.
/// Un redémarrage ne perd rien, et il n'y a rien à persister.
/// </remarks>
public sealed record MediaSnapshot(
    IReadOnlyList<MediaJourney> Journeys,
    IReadOnlyList<string> UnavailableSources,
    DateTimeOffset? ObservedAt)
{
    public static MediaSnapshot Empty { get; } = new([], [], null);

    /// <summary>Parcours ayant au moins un téléchargement en cours.</summary>
    public IEnumerable<MediaJourney> Active =>
        Journeys.Where(j => j.Downloads.Any(d => d.State is DownloadState.Downloading
                                                        or DownloadState.Importing));

    /// <summary>
    /// Les parcours les plus dignes d'attention, triés et bornés.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Le classement vit ici, pas dans les adaptateurs.</b> Si chacun décidait de ce qui est
    /// « intéressant », le message Discord et la page web afficheraient des choses différentes
    /// du même instant — et la divergence ne se verrait qu'en les comparant côte à côte.
    /// </para>
    /// <para>
    /// Critère : <b>ce qui va mal d'abord, puis ce qui est le plus proche d'aboutir.</b> Un
    /// téléchargement bloqué mérite d'être vu avant un téléchargement sain à 3 % — le premier
    /// demande une décision, le second demande de la patience.
    /// </para>
    /// <para>
    /// <b>Un média déjà disponible est exclu</b>, sauf s'il demande une intervention. Ce filtre
    /// n'est pas cosmétique : sans lui, un parcours disponible ayant par construction une
    /// progression de 1,0, les médias terminés écrasent au tri tout ce qui télécharge. Constaté
    /// en conditions réelles — sur 49 parcours dont un seul actif, le palmarès affichait cinq
    /// médias à 100 % et masquait le seul téléchargement en cours.
    /// </para>
    /// <para>
    /// Le palmarès peut donc compter moins de <paramref name="take"/> entrées, et c'est le
    /// comportement voulu : mieux vaut une liste courte qu'une liste complétée par du bruit.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MediaJourney> MostInteresting(int take) =>
        [.. Journeys
            .Where(j => j.NeedsAttention || j.State is JourneyState.Downloading
                                                    or JourneyState.Importing)
            .OrderByDescending(j => j.NeedsAttention)
            // L'import passe avant le téléchargement : c'est ce qui est sur le point d'aboutir.
            .ThenByDescending(j => j.State == JourneyState.Importing)
            .ThenByDescending(j => j.Progress)
            .ThenBy(j => j.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)];

    /// <summary>Parcours demandant une intervention.</summary>
    public int AttentionCount => Journeys.Count(j => j.NeedsAttention);

    /// <summary>
    /// Octets restants, agrégés correctement.
    /// </summary>
    /// <remarks>
    /// Le regroupement par téléchargement a déjà eu lieu : sommer ici ne peut plus compter la
    /// même taille vingt-deux fois (ADR-0015).
    /// </remarks>
    public long BytesRemaining => Journeys.SelectMany(j => j.Downloads).Sum(d => d.SizeLeft);

    public long BytesTotal => Journeys.SelectMany(j => j.Downloads).Sum(d => d.Size);
}

/// <summary>
/// Un média, de la demande à la disponibilité.
/// </summary>
/// <param name="Key">Identité stable : <c>movie:49</c>, <c>series:21</c>.</param>
/// <param name="MediaType">Film ou série.</param>
/// <param name="Title">Titre affichable, pris à la meilleure source disponible.</param>
/// <param name="TmdbId">Référence publique, conservée pour l'affichage et les liens.</param>
/// <param name="TvdbId">Référence publique des séries.</param>
/// <param name="Request">
/// Demande Seerr à l'origine du parcours. <b>Nulle pour un import manuel</b> — ce n'est pas une
/// anomalie, seulement un parcours sans demandeur.
/// </param>
/// <param name="Downloads">
/// Téléchargements, un par <c>downloadId</c>. <b>Vide pour un média déjà disponible</b> dont
/// rien n'a jamais été téléchargé par le hub.
/// </param>
/// <param name="State">État global, dérivé des téléchargements et de la demande.</param>
public sealed record MediaJourney(
    string Key,
    MediaKind MediaType,
    string? Title,
    int? TmdbId,
    int? TvdbId,
    MediaRequest? Request,
    IReadOnlyList<DownloadItem> Downloads,
    JourneyState State)
{
    /// <summary>
    /// Progression agrégée <b>sur les octets</b>, jamais en moyennant des pourcentages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La simplification tentante — <c>Downloads.Average(d => d.Progress)</c> — est fausse dès
    /// que les téléchargements ont des tailles différentes, ce qui est le cas courant. Un pack
    /// de 20 Go à 10 % et un épisode de 1 Go à 90 % donnent 50 % en moyenne de pourcentages,
    /// alors qu'il reste 18,1 Go sur 21, soit 14 % réellement téléchargés. L'utilisateur qui
    /// lit « 50 % » attendra deux fois moins longtemps que la réalité.
    /// </para>
    /// <para>
    /// Un lecteur futur remplacera peut-être ce calcul par la moyenne, de bonne foi, en le
    /// prenant pour une complication inutile. C'est pourquoi le test
    /// <c>Requete_de_saison_resolue_en_episodes_separes_donne_N_telechargements_agreges</c>
    /// vérifie une valeur que la moyenne ne produit pas.
    /// </para>
    /// </remarks>
    public double Progress
    {
        get
        {
            var total = Downloads.Sum(d => d.Size);
            if (total <= 0)
            {
                return State == JourneyState.Available ? 1d : 0d;
            }

            return Math.Clamp((double)(total - Downloads.Sum(d => d.SizeLeft)) / total, 0d, 1d);
        }
    }

    /// <summary>Débit cumulé, lu chez qBittorrent quand le torrent est connu.</summary>
    public long DownloadSpeed => Downloads.Sum(d => d.Torrent?.DownloadSpeed ?? 0);

    /// <summary>
    /// Ce parcours demande-t-il une intervention ?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fondé sur les seuls faits dont la sémantique est établie : la santé rapportée par
    /// <c>trackedDownloadStatus</c>, et un état terminal négatif ou indéterminé. Ni
    /// <c>status</c>, ni <c>errorMessage</c>, ni <c>statusMessages</c> n'entrent ici — le
    /// premier vaut <c>warning</c> dès la première seconde, et les deux autres n'ont pas encore
    /// été observés sur un cas réellement bloqué (ADR-0015).
    /// </para>
    /// <para>
    /// Ce n'est <b>pas</b> le moteur d'anomalies : il n'y a ni seuil de durée, ni déduplication,
    /// ni mise en sommeil. C'est un critère de tri, destiné à faire remonter en tête ce qui
    /// mérite un regard. Le moteur viendra à l'étape 4 et pourra enrichir ce signal.
    /// </para>
    /// </remarks>
    public bool NeedsAttention =>
        State is JourneyState.Failed or JourneyState.Unresolved
        || Downloads.Any(d => d.Health is DownloadHealth.Warning or DownloadHealth.Error);
}

/// <summary>
/// Un téléchargement : un torrent, et les N entrées de file qui le référencent.
/// </summary>
/// <remarks>
/// <b>C'est le niveau de regroupement qui manquait au modèle initial</b> (ADR-0015). Un pack de
/// saison produit une entrée de file par épisode, toutes porteuses du même <c>downloadId</c> et
/// de la même taille répétée. <see cref="Size"/> est donc pris <i>une fois</i>, jamais sommé.
/// </remarks>
/// <param name="DownloadId">Hash normalisé en minuscules — la clé de jointure.</param>
/// <param name="Title">Titre de la release.</param>
/// <param name="Size">Taille du torrent, prise sur une seule entrée.</param>
/// <param name="SizeLeft">Reste à télécharger, pris sur une seule entrée.</param>
/// <param name="State">Étape du cycle, dérivée de <c>trackedDownloadState</c>.</param>
/// <param name="Health">Santé, dérivée de <c>trackedDownloadStatus</c> — le seul axe fiable.</param>
/// <param name="Torrent">Torrent correspondant, ou <c>null</c> s'il a déjà été retiré du client.</param>
/// <param name="Episodes">Épisodes couverts. Vide pour un film.</param>
/// <param name="AddedAt">Ajout à la file, base du délai de grâce des détecteurs.</param>
/// <param name="Terminal">
/// Issue lue dans l'historique, quand l'entrée de file a disparu. <c>null</c> tant que le
/// téléchargement est encore dans la file.
/// </param>
public sealed record DownloadItem(
    string DownloadId,
    string? Title,
    long Size,
    long SizeLeft,
    DownloadState State,
    DownloadHealth Health,
    QBittorrentTorrent? Torrent,
    IReadOnlyList<EpisodeReference> Episodes,
    DateTimeOffset? AddedAt,
    TerminalOutcome? Terminal)
{
    public double Progress => Size <= 0 ? 0d : Math.Clamp((double)(Size - SizeLeft) / Size, 0d, 1d);

    /// <summary>
    /// Le téléchargement a-t-il déjà progressé ?
    /// </summary>
    /// <remarks>
    /// Sert à ne pas crier au blocage sur un torrent qui vient d'être récupéré et n'a pas encore
    /// trouvé de pair — il se déclare « stalled with no connections » dès la première seconde
    /// (ADR-0015).
    /// </remarks>
    public bool HasProgressed => SizeLeft < Size;
}

/// <param name="EpisodeId">Identifiant Sonarr de l'épisode.</param>
/// <param name="SeasonNumber">Saison.</param>
/// <param name="EpisodeNumber">Numéro dans la saison.</param>
public sealed record EpisodeReference(int EpisodeId, int SeasonNumber, int EpisodeNumber);

/// <param name="RequestId">Identifiant de la requête Seerr.</param>
/// <param name="RequestedAt">Date de la demande.</param>
/// <param name="Seasons">Saisons demandées. Plusieurs sont possibles.</param>
public sealed record MediaRequest(int RequestId, DateTimeOffset RequestedAt, IReadOnlyList<int> Seasons);

public enum MediaKind
{
    Movie = 0,
    Series = 1,
}

public enum JourneyState
{
    /// <summary>Demandé, rien n'a encore été récupéré.</summary>
    Requested = 0,

    /// <summary>Au moins un téléchargement en cours.</summary>
    Downloading = 1,

    /// <summary>Téléchargé, en attente ou en cours d'import.</summary>
    Importing = 2,

    /// <summary>Disponible dans la bibliothèque.</summary>
    Available = 3,

    /// <summary>Le dernier téléchargement a échoué ou a été ignoré.</summary>
    Failed = 4,

    /// <summary>
    /// L'entrée de file a disparu sans qu'aucun événement terminal soit lisible.
    /// </summary>
    /// <remarks>
    /// État transitoire, pas un état de repos : le repli ciblé sur l'historique doit le
    /// résoudre. S'il persiste, c'est que l'événement est sorti de la fenêtre d'historique et
    /// que la page doit être agrandie.
    /// </remarks>
    Unresolved = 5,
}

public enum DownloadState
{
    Downloading = 0,
    Importing = 1,
    Completed = 2,
    Unknown = 3,
}

/// <summary>
/// Santé d'un téléchargement, dérivée du seul champ dont la sémantique est établie.
/// </summary>
/// <remarks>
/// <c>status</c> et <c>errorMessage</c> sont délibérément ignorés : le premier vaut
/// <c>warning</c> dès la première seconde, le second annonce « stalled with no connections »
/// sur un téléchargement parfaitement sain. <c>statusMessages</c> reste inexploité tant qu'un
/// import réellement bloqué n'aura pas été observé (ADR-0015).
/// </remarks>
public enum DownloadHealth
{
    Ok = 0,
    Warning = 1,
    Error = 2,
    Unknown = 3,
}

public enum TerminalOutcome
{
    Imported = 0,
    Failed = 1,
    Ignored = 2,
}
