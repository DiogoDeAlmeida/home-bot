using HomelabHub.Abstractions.Configuration;
using HomelabHub.Abstractions.Modules;
using HomelabHub.Modules.SystemInfo.Capabilities;

namespace HomelabHub.Modules.SystemInfo;

/// <summary>
/// Le hub observé par lui-même : version, disponibilité, espace disque, sauvegardes.
/// </summary>
/// <remarks>
/// <para>
/// Ce module est le <b>banc de test de l'abstraction</b>. Il est réel, il sert en production, et
/// il est assez trivial pour que ce qui coince vienne du contrat et non de sa complexité propre.
/// Il a été écrit avant l'interface web, délibérément : poser le contrat puis construire le
/// socle en aveugle aurait garanti une réécriture.
/// </para>
/// <para>
/// Ce qu'il a déjà démontré : un module n'a besoin de rien d'autre que
/// <c>HomelabHub.Abstractions</c>, y compris pour déclencher une sauvegarde du hub — les
/// services de plateforme sont dans les contrats, pas dans le noyau.
/// </para>
/// </remarks>
public sealed class SystemModule : IHubModule
{
    /// <summary>Seuil d'espace libre, en pourcentage, sous lequel une anomalie est ouverte.</summary>
    public const string FreeSpaceThresholdKey = "disk.warnBelowPercent";

    /// <summary>Cadence d'observation.</summary>
    public const string IntervalKey = "pollIntervalSeconds";

    public string Key => "system";

    public string DisplayName => "Système";

    public string Description => "État du hub : version, disponibilité, espace disque, sauvegardes.";

    public ModuleConfigSchema ConfigSchema => new ModuleConfigSchema()
        .AddInt(FreeSpaceThresholdKey, "Alerte sous (% d'espace libre)", defaultValue: 10,
                help: "Une anomalie est ouverte tant qu'un volume passe sous ce seuil.")
        .AddDuration(IntervalKey, "Intervalle d'observation", TimeSpan.FromMinutes(1),
                     help: "Le module ne sollicite que le système de fichiers local : inutile d'être agressif.");

    public void Register(IModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddState(SystemSnapshot.Empty)
               .AddPoller<SystemPoller>(TimeSpan.FromMinutes(1), IntervalKey)
               .AddHealthCheck<SystemHealthCheck>()
               .AddCapability<StatusCapability>()
               .AddCapability<DiskCapability>()
               .AddCapability<CreateBackupCapability>()
               .AddCapability<ListBackupsCapability>()
               .AddWidget<SystemOverviewWidget>();
    }
}
