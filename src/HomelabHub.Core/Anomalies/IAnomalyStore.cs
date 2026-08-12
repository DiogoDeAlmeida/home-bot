namespace HomelabHub.Core.Anomalies;

/// <summary>
/// Ce qui fait survivre la table d'anomalies à un redémarrage.
/// </summary>
/// <remarks>
/// <para>
/// C'est le premier état réellement possédé par le hub. « Ouverte depuis dix heures » ne se
/// redemande à personne : ni Radarr ni qBittorrent ne savent depuis quand une situation dure du
/// point de vue du hub. Sans ce magasin, chaque redémarrage rouvrait tout et renotifiait tout.
/// </para>
/// <para>
/// Le noyau ne connaît ni SQLite ni EF Core : il connaît trois opérations. L'implémentation par
/// défaut ne fait rien, ce qui garde les tests du moteur sans base et sans montage.
/// </para>
/// </remarks>
public interface IAnomalyStore
{
    /// <summary>Anomalies conservées, à charger au démarrage.</summary>
    IReadOnlyList<Anomaly> Load();

    /// <summary>Écrit ou met à jour les anomalies passées. Appelée hors du verrou du moteur.</summary>
    void Save(IReadOnlyList<Anomaly> anomalies);

    /// <summary>
    /// Supprime les anomalies résolues depuis plus longtemps que la fenêtre donnée.
    /// </summary>
    /// <returns>Nombre de lignes supprimées.</returns>
    int PurgeResolvedBefore(DateTimeOffset cutoff);
}

/// <summary>Magasin qui n'écrit rien : le comportement d'avant la base, pour les tests.</summary>
internal sealed class NullAnomalyStore : IAnomalyStore
{
    public IReadOnlyList<Anomaly> Load() => [];

    public void Save(IReadOnlyList<Anomaly> anomalies)
    {
        // Volontairement vide.
    }

    public int PurgeResolvedBefore(DateTimeOffset cutoff) => 0;
}
