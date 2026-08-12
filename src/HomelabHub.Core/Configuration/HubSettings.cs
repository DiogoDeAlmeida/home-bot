using HomelabHub.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Configuration;

/// <summary>
/// Réglages du hub lui-même, décrits avec la même primitive que ceux d'un module (ADR-0013).
/// </summary>
/// <remarks>
/// Aux yeux de l'interface, le noyau est un pseudo-module : même schéma, même endpoint, même
/// générateur de formulaire. Dans le contrat, il n'en est pas un — il n'implémente pas
/// <c>IHubModule</c>, n'a ni capacité ni cycle d'ingestion.
/// </remarks>
public static class HubSettings
{
    /// <summary>Préfixe réservé. Aucun module ne peut revendiquer cette clé.</summary>
    public const string Prefix = "hub";

    public const string BackupRetentionKey = "hub.backup.retention";
    public const string BackupMinimumIntervalKey = "hub.backup.minimumInterval";
    public const string LogLevelKey = "hub.logging.level";
    public const string JournalRetentionDaysKey = "hub.journal.retentionDays";
    public const string JournalMaximumRowsKey = "hub.journal.maxRows";
    public const string DiscordTokenKey = "hub.discord.token";
    public const string DiscordGuildIdKey = "hub.discord.guildId";
    public const string DiscordDashboardChannelIdKey = "hub.discord.dashboardChannelId";
    public const string DiscordAdminRoleIdKey = "hub.discord.adminRoleId";

    /// <summary>
    /// Identifiant du message de tableau de bord, réutilisé pour l'éditer en place plutôt que
    /// d'en reposter un à chaque cycle. Non exposé au schéma : c'est un état écrit par
    /// l'adaptateur, pas un réglage saisi par l'exploitant.
    /// </summary>
    public const string DiscordDashboardMessageIdKey = "hub.discord.dashboardMessageId";

    /// <summary>
    /// Les clés du schéma sont relatives — <c>backup.retention</c> — et préfixées par
    /// <see cref="Prefix"/> à l'écriture, exactement comme pour un module.
    /// </summary>
    public static HubConfigSchema Schema { get; } = new HubConfigSchema()
        .AddInt("backup.retention", "Sauvegardes conservées", defaultValue: 10,
                help: "Les archives les plus anciennes sont supprimées au-delà de ce nombre.")
        .AddDuration("backup.minimumInterval", "Intervalle minimal entre deux sauvegardes",
                     TimeSpan.FromMinutes(5),
                     help: "Anti-rebond appliqué aux sauvegardes demandées par un module.")
        .AddSelect("logging.level", "Niveau de journalisation",
                   options:
                   [
                       new ConfigOption(nameof(LogLevel.Trace), "Trace — très verbeux"),
                       new ConfigOption(nameof(LogLevel.Debug), "Debug — diagnostic"),
                       new ConfigOption(nameof(LogLevel.Information), "Information — normal"),
                       new ConfigOption(nameof(LogLevel.Warning), "Avertissement"),
                       new ConfigOption(nameof(LogLevel.Error), "Erreur seulement"),
                   ],
                   defaultValue: nameof(LogLevel.Information),
                   help: "Prend effet immédiatement, sans redémarrage ni accès SSH.")
        .AddInt("journal.retentionDays", "Rétention du journal (jours)", defaultValue: 14,
                help: "Les événements plus anciens sont supprimés par la purge quotidienne.")
        .AddInt("journal.maxRows", "Lignes de journal conservées", defaultValue: 100_000,
                help: "Seconde borne : la première des deux limites atteinte l'emporte.")
        .AddSecret("discord.token", "Jeton du bot Discord", required: false,
                   help: "Application dédiée au hub, distincte de Doplarr. Absent : l'adaptateur reste éteint.")
        .AddText("discord.guildId", "ID du serveur Discord", required: false,
                 help: "Les commandes sont enregistrées en guild, pour un effet immédiat.")
        .AddText("discord.dashboardChannelId", "ID du salon du tableau de bord", required: false,
                 help: "Le message y est édité en place, jamais reposté.")
        .AddText("discord.adminRoleId", "ID du rôle hub-admin", required: false,
                 help: "Seul ce rôle peut déclencher une mutation depuis Discord (ADR-0004).");
}

/// <summary>
/// Niveau de journalisation modifiable à chaud.
/// </summary>
/// <remarks>
/// <para>
/// <c>ILoggingBuilder.AddFilter</c> reçoit un délégué évalué à chaque appel de journalisation :
/// il suffit qu'il lise cette propriété pour que le changement soit instantané. C'est ce qui
/// permet de passer en <c>Debug</c> depuis l'interface quand quelque chose cloche, sans SSH et
/// sans redémarrer le service.
/// </para>
/// <para>
/// <b>Ce délégué doit être le seul filtre.</b> Une section <c>Logging:LogLevel</c> dans
/// <c>appsettings.json</c> installe des règles concurrentes qui l'emportent silencieusement — le
/// réglage de l'interface semble alors accepté sans rien changer. La section a donc été retirée,
/// et le plancher appliqué au bruit du framework est encodé ici, en un seul endroit.
/// </para>
/// </remarks>
public sealed class LogLevelSwitch
{
    private volatile int _minimum = (int)LogLevel.Information;

    public LogLevel Minimum
    {
        get => (LogLevel)_minimum;
        set => _minimum = (int)value;
    }

    /// <param name="category">Catégorie du journal, généralement le nom du type émetteur.</param>
    /// <param name="level">Niveau de l'appel.</param>
    public bool IsEnabled(string? category, LogLevel level)
    {
        if (level < Minimum)
        {
            return false;
        }

        // ASP.NET Core et le framework sont très bavards en Information. On ne les écoute qu'à
        // partir de Warning — sauf quand l'exploitant a explicitement demandé du diagnostic,
        // auquel cas il veut précisément tout voir.
        if (Minimum > LogLevel.Debug
            && category?.StartsWith("Microsoft.", StringComparison.Ordinal) == true)
        {
            return level >= LogLevel.Warning;
        }

        return true;
    }

    /// <summary>Applique la valeur stockée, ou <c>Information</c> si elle est absente ou illisible.</summary>
    public void ApplyFrom(IHubConfigStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        Minimum = Enum.TryParse<LogLevel>(store.GetValue(HubSettings.LogLevelKey), out var parsed)
            ? parsed
            : LogLevel.Information;
    }
}
