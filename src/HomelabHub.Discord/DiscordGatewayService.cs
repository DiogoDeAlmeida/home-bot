using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using global::Discord;
using global::Discord.WebSocket;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Dashboard;
using HomelabHub.Core.Anomalies;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Core.Modules;
using HomelabHub.Discord.Commands;
using HomelabHub.Discord.Dashboard;
using HomelabHub.Discord.Interactions;
using HomelabHub.Discord.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Discord;

/// <summary>
/// Tient la passerelle Discord : connexion, enregistrement des commandes, routage des
/// interactions vers <see cref="ICapabilityExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>L'adaptateur ne connaît que le genre de surface, jamais une règle de sécurité</b>
/// (ADR-0004, ADR-0016) : il calcule <c>IsAdministrator</c> en comparant les rôles du membre
/// Discord au rôle configuré, et transmet ce verdict à l'exécuteur, qui seul décide. Aucune
/// capacité n'est vérifiée deux fois — y compris au clic du bouton « Confirmer », qui repasse
/// par la même vérification avec l'identité de qui a cliqué, pas de qui a tapé la commande.
/// </para>
/// <para>
/// <b>Sans jeton ni serveur configurés, le service reste éteint</b> plutôt que d'échouer au
/// démarrage : Discord est une surface parmi d'autres, pas une dépendance du hub lui-même. Le
/// journal le dit une fois, et le service se termine sans boucler.
/// </para>
/// <para>
/// <b>Aucune exception ne doit s'échapper d'un gestionnaire d'interaction</b> (convention §14) :
/// une panne dégrade la réponse Discord, elle ne doit jamais faire tomber le processus qui porte
/// aussi l'API web et l'ingestion.
/// </para>
/// </remarks>
internal sealed class DiscordGatewayService(
    IHubConfigStore config,
    ICapabilityRegistry capabilities,
    ICapabilityExecutor executor,
    IAnomalyEngine anomalies,
    ModuleCatalog catalog,
    IModuleRegistry modules,
    IServiceProvider services,
    ILogger<DiscordGatewayService> logger) : BackgroundService, IAnomalyNotifier
{
    /// <summary>
    /// Cadence du tableau de bord, alignée sur l'intervalle par défaut du poller média : pas de
    /// raison de rafraîchir plus souvent que la donnée elle-même ne change.
    /// </summary>
    private static readonly TimeSpan DashboardRefreshInterval = TimeSpan.FromSeconds(60);

    private DiscordSocketClient? _client;
    private DiscordCommandPlan _plan = new([], new Dictionary<string, string>());

    /// <summary>
    /// Message Discord de chaque anomalie active, par clé de déduplication.
    /// </summary>
    /// <remarks>
    /// <b>En mémoire, non persisté</b> — simplification assumée pour cette tranche. Une anomalie
    /// encore ouverte au moment d'un redémarrage du hub obtiendra un nouveau message plutôt que
    /// de continuer à éditer l'ancien : une duplication cosmétique, pas une perte d'information —
    /// la table d'anomalies elle-même reste correcte (ADR-0017). Un dictionnaire concurrent parce
    /// que <see cref="NotifyAsync"/> est appelée depuis autant de boucles de cycle qu'il y a de
    /// modules.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ulong> _anomalyMessages = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = config.GetValue(HubSettings.DiscordTokenKey);
        var guildId = config.GetValue(HubSettings.DiscordGuildIdKey);

        if (string.IsNullOrWhiteSpace(token) || !ulong.TryParse(guildId, out var guild))
        {
            logger.LogInformation(
                "Adaptateur Discord éteint : jeton ou identifiant de serveur absent des réglages.");
            return;
        }

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // Aucune donnée de message n'est lue : les interactions (commandes, boutons)
            // portent tout ce dont l'adaptateur a besoin, y compris le membre qui les déclenche.
            GatewayIntents = GatewayIntents.Guilds,
        });
        _client = client;

        client.Log += OnDiscordLog;
        client.Ready += () => OnReadyAsync(client, guild);
        client.SlashCommandExecuted += OnSlashCommandAsync;
        client.ButtonExecuted += OnButtonAsync;

        try
        {
            await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);

            // Remplace l'attente passive : le tableau de bord se rafraîchit à intervalle
            // régulier tant que le hub tourne, et cette boucle tient le service vivant entre
            // deux passages, exactement comme l'aurait fait un Task.Delay infini.
            using var timer = new PeriodicTimer(DashboardRefreshInterval);

            do
            {
                await RefreshDashboardAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt normal du hub.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connexion à Discord impossible.");
        }
        finally
        {
            await client.StopAsync().ConfigureAwait(false);
            await client.LogoutAsync().ConfigureAwait(false);
        }
    }

    private async Task OnReadyAsync(DiscordSocketClient client, ulong guildId)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            logger.LogError(
                "Serveur Discord {GuildId} introuvable — le bot y est-il invité ?", guildId);
            return;
        }

        // Exposure.Chat garantit déjà que Command n'est jamais posé sans lui (CapabilityValidator) :
        // filtrer sur la seule présence d'un Command suffit.
        _plan = DiscordCommandBuilder.Build(capabilities.All);

        try
        {
            // Nom singulier sur SocketGuild, au singulier différent de IGuild — vérifié par
            // réflexion sur l'assembly 3.20.1 après un premier essai resté sur le pluriel.
            await guild.BulkOverwriteApplicationCommandAsync([.. _plan.Commands]).ConfigureAwait(false);
            logger.LogInformation("{Count} commande(s) enregistrée(s) sur le serveur {GuildId}.",
                _plan.Commands.Count, guildId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Enregistrement des commandes Discord impossible.");
        }
    }

    /// <summary>
    /// Poste ou édite en place le message de tableau de bord d'un salon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// L'identifiant du message est conservé via <see cref="IHubConfigStore"/>
    /// (<see cref="HubSettings.DiscordDashboardMessageIdKey"/>) : c'est ce qui permet de le
    /// retrouver après un redémarrage plutôt que d'en poster un nouveau à chaque fois, ce qui
    /// noierait le salon.
    /// </para>
    /// <para>
    /// Un message supprimé côté Discord — à la main, ou parce que le salon a été vidé — ne doit
    /// pas bloquer le tableau de bord pour toujours : l'édition qui échoue en 404 déclenche la
    /// republication d'un message neuf, dont l'identifiant remplace l'ancien.
    /// </para>
    /// </remarks>
    private async Task RefreshDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Peut être null au tout premier passage, avant que Ready n'ait résolu le cache de
            // guildes : le cycle suivant, soixante secondes plus tard, réessaiera de lui-même.
            if (ResolveDashboardChannel() is not { } channel)
            {
                return;
            }

            var widgets = await CollectDashboardWidgetsAsync(cancellationToken).ConfigureAwait(false);
            var content = BuildDashboardContent(widgets);

            var messageIdRaw = config.GetValue(HubSettings.DiscordDashboardMessageIdKey);

            if (ulong.TryParse(messageIdRaw, out var messageId))
            {
                try
                {
                    await channel.ModifyMessageAsync(messageId, props => props.Content = content)
                                 .ConfigureAwait(false);
                    return;
                }
                // Écrit en toutes lettres : « Discord » nu résoudrait vers notre propre espace
                // de noms HomelabHub.Discord depuis ce fichier (même piège que Discord.WebSocket
                // plus haut), pas vers Discord.Net.
                catch (global::Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                {
                    // Le message a été supprimé côté Discord : republié plus bas.
                }
            }

            var posted = await channel.SendMessageAsync(content).ConfigureAwait(false);
            await config.SetAsync(HubSettings.DiscordDashboardMessageIdKey,
                posted.Id.ToString(CultureInfo.InvariantCulture), secret: false, cancellationToken)
                        .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rafraîchissement du tableau de bord Discord impossible.");
        }
    }

    /// <summary>Le salon du tableau de bord, ou <c>null</c> tant qu'il n'est pas résolvable.</summary>
    /// <remarks>
    /// Partagé par le tableau de bord et les notifications d'anomalie : les deux vivent dans le
    /// même salon, et re-résoudre à chaque appel plutôt que de garder une référence coûte une
    /// consultation de configuration, largement moins cher qu'un appel réseau.
    /// </remarks>
    private SocketTextChannel? ResolveDashboardChannel()
    {
        if (_client is not { } client)
        {
            return null;
        }

        var guildIdRaw = config.GetValue(HubSettings.DiscordGuildIdKey);
        var channelIdRaw = config.GetValue(HubSettings.DiscordDashboardChannelIdKey);

        return ulong.TryParse(guildIdRaw, out var guildId) && ulong.TryParse(channelIdRaw, out var channelId)
            ? client.GetGuild(guildId)?.GetTextChannel(channelId)
            : null;
    }

    /// <summary>
    /// Un message par anomalie, édité en place à chaque transition — Opened l'ouvre, Escalated
    /// et Reopened l'éditent, Resolved le clôt visuellement puis libère son suivi.
    /// </summary>
    public async Task NotifyAsync(AnomalyTransition transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (ResolveDashboardChannel() is not { } channel)
        {
            return;
        }

        var anomaly = transition.Anomaly;
        var content = DiscordAnomalyRenderer.Render(anomaly);
        var components = anomaly.State == AnomalyState.Open
            ? BuildSnoozeButtons(anomaly.DedupeKey)
            : new ComponentBuilder().Build();

        await PostOrEditAnomalyMessageAsync(channel, anomaly.DedupeKey, content, components).ConfigureAwait(false);

        if (transition.Kind == AnomalyTransitionKind.Resolved)
        {
            // Une anomalie résolue garde son message — la dernière chose affichée doit rester
            // « résolue », pas disparaître — mais un futur épisode du même DedupeKey rouvrira un
            // message neuf plutôt que de réutiliser celui-ci pour une histoire qui n'est plus la
            // même.
            _anomalyMessages.TryRemove(anomaly.DedupeKey, out _);
        }
    }

    private async Task PostOrEditAnomalyMessageAsync(SocketTextChannel channel, string dedupeKey,
                                                      string content, MessageComponent components)
    {
        if (_anomalyMessages.TryGetValue(dedupeKey, out var messageId))
        {
            try
            {
                await channel.ModifyMessageAsync(messageId, props =>
                {
                    props.Content = content;
                    props.Components = components;
                }).ConfigureAwait(false);
                return;
            }
            catch (global::Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                _anomalyMessages.TryRemove(dedupeKey, out _);
            }
        }

        var posted = await channel.SendMessageAsync(content, components: components).ConfigureAwait(false);
        _anomalyMessages[dedupeKey] = posted.Id;
    }

    private static MessageComponent BuildSnoozeButtons(string dedupeKey) =>
        new ComponentBuilder()
            .WithButton("Ignorer 6 h", DiscordSnoozeButtons.Encode(dedupeKey, hours: 6), ButtonStyle.Secondary)
            .WithButton("Jusqu'à résolution", DiscordSnoozeButtons.Encode(dedupeKey, hours: null), ButtonStyle.Secondary)
            .Build();

    /// <remarks>
    /// Même filtre que <c>/api/widgets</c> côté REST — modules actifs, widgets marqués pour le
    /// tableau de bord — pour que les deux surfaces montrent la même sélection (ADR-0006). Un
    /// widget en panne laisse un trou plutôt que de faire échouer tout le rafraîchissement.
    /// </remarks>
    private async Task<IReadOnlyList<WidgetPayload>> CollectDashboardWidgetsAsync(
        CancellationToken cancellationToken)
    {
        var payloads = new List<(int Order, WidgetPayload Widget)>();

        foreach (var module in catalog.Descriptors.Where(m => modules.IsActive(m.Key)))
        {
            foreach (var type in module.WidgetTypes)
            {
                var widget = (IWidgetProvider)services.GetRequiredService(type);
                if (!widget.Descriptor.ShowOnChatDashboard)
                {
                    continue;
                }

                try
                {
                    var payload = await widget.GetAsync(cancellationToken).ConfigureAwait(false);
                    payloads.Add((widget.Descriptor.Order, payload));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Widget {Key} indisponible pour le tableau de bord Discord.",
                        widget.Descriptor.Key);
                }
            }
        }

        return [.. payloads.OrderBy(p => p.Order).Select(p => p.Widget)];
    }

    private static string BuildDashboardContent(IReadOnlyList<WidgetPayload> widgets)
    {
        if (widgets.Count == 0)
        {
            return "_Aucun widget actif._";
        }

        var blocks = widgets.Select(DiscordWidgetRenderer.Render);

        // UTC, pas Europe/Paris : le hub raisonne en UTC de bout en bout (ADR-0017) et n'a
        // aujourd'hui aucune conversion de fuseau. Étiqueté explicitement pour ne pas laisser
        // croire à une heure locale.
        var footer = string.Create(CultureInfo.InvariantCulture,
            $"_Mis à jour à {DateTimeOffset.UtcNow:HH:mm} UTC_");

        return string.Join("\n\n", blocks) + "\n\n" + footer;
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        try
        {
            var options = command.Data.Options?.Select(InteractionOption.FromDiscord).ToList();
            var (route, arguments) = DiscordInteractionRoute.Read(command.Data.Name, options);

            if (!_plan.RouteToCapabilityKey.TryGetValue(route, out var capabilityKey))
            {
                await command.RespondAsync("Commande inconnue de ce côté-ci.", ephemeral: true)
                             .ConfigureAwait(false);
                return;
            }

            var registered = capabilities.Find(capabilityKey);

            // RequireConfirmation est une propriété de l'opération, pas du canal (ADR-0016) :
            // une commande tapée mérite la même pause qu'un clic de bouton mal engagé.
            if (registered?.Descriptor.RequireConfirmation == true)
            {
                await RespondWithConfirmationAsync(command, registered.Descriptor, arguments)
                    .ConfigureAwait(false);
                return;
            }

            var privateReply = registered?.Descriptor.Command?.PrivateReply ?? false;
            var result = await RunAsync(capabilityKey, arguments, InvocationSource.ChatCommand, command.User)
                .ConfigureAwait(false);

            await command.RespondAsync(Render(result), ephemeral: privateReply || IsFailure(result))
                         .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Convention §14 : ce gestionnaire ne doit jamais laisser tomber le processus.
            logger.LogError(ex, "Échec du traitement d'une interaction Discord.");

            if (!command.HasResponded)
            {
                await command.RespondAsync("Une erreur inattendue a interrompu la commande.", ephemeral: true)
                             .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Le prompt reste toujours éphémère, indépendamment de <c>PrivateReply</c> : Discord
    /// n'autorise de toute façon que l'auteur d'un message éphémère à en cliquer les boutons,
    /// ce qui rend superflue toute vérification supplémentaire de « qui a le droit de
    /// confirmer ». <c>PrivateReply</c> ne joue qu'au moment du résultat, une fois exécuté.
    /// </summary>
    private static async Task RespondWithConfirmationAsync(
        SocketSlashCommand command, CapabilityDescriptor descriptor,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var components = new ComponentBuilder()
            .WithButton("Confirmer", DiscordConfirmationToken.Encode(descriptor.Key, arguments), ButtonStyle.Danger)
            .WithButton("Annuler", DiscordConfirmationToken.Cancel, ButtonStyle.Secondary)
            .Build();

        await command.RespondAsync($"Confirmer « {descriptor.DisplayName} » ?",
                                   components: components, ephemeral: true)
                     .ConfigureAwait(false);
    }

    private async Task OnButtonAsync(SocketMessageComponent component)
    {
        try
        {
            var customId = component.Data.CustomId;

            if (string.Equals(customId, DiscordConfirmationToken.Cancel, StringComparison.Ordinal))
            {
                await component.UpdateAsync(props =>
                {
                    props.Content = "Annulé.";
                    props.Components = new ComponentBuilder().Build();
                }).ConfigureAwait(false);
                return;
            }

            if (DiscordSnoozeButtons.TryDecode(customId) is { } snooze)
            {
                await OnSnoozeButtonAsync(component, snooze.DedupeKey, snooze.Hours).ConfigureAwait(false);
                return;
            }

            if (DiscordConfirmationToken.TryDecode(customId) is not { } action)
            {
                await component.RespondAsync("Bouton inconnu de ce côté-ci.", ephemeral: true)
                               .ConfigureAwait(false);
                return;
            }

            var registered = capabilities.Find(action.CapabilityKey);
            var privateReply = registered?.Descriptor.Command?.PrivateReply ?? false;

            var result = await RunAsync(action.CapabilityKey, action.Arguments,
                InvocationSource.ChatButton, component.User).ConfigureAwait(false);

            await component.UpdateAsync(props =>
            {
                props.Content = Render(result);
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);

            // Le prompt reste éphémère, visible du seul appelant. Sans PrivateReply, l'issue
            // rejoint aussi le salon — comme une exécution directe, sans confirmation, l'aurait
            // fait : confirmer ne doit pas rendre un résultat public moins visible qu'il ne
            // l'aurait été.
            if (!privateReply && !IsFailure(result))
            {
                await component.Channel.SendMessageAsync(Render(result)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Échec du traitement d'un bouton Discord.");

            if (!component.HasResponded)
            {
                await component.RespondAsync("Une erreur inattendue a interrompu l'action.", ephemeral: true)
                               .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// <c>hub.anomaly.snooze</c> n'exige pas de confirmation : le clic exécute directement.
    /// </summary>
    /// <remarks>
    /// La mise en sommeil ne passe pas par un cycle d'ingestion — elle ne produit donc aucune
    /// <see cref="AnomalyTransition"/> que <see cref="NotifyAsync"/> aurait reçue. Sans ce
    /// rafraîchissement explicite, le message de l'anomalie continuerait d'afficher « ouverte »,
    /// boutons actifs compris, jusqu'à sa prochaine vraie transition — ce qui aurait fait
    /// paraître le clic sans effet.
    /// </remarks>
    private async Task OnSnoozeButtonAsync(SocketMessageComponent component, string dedupeKey, int? hours)
    {
        var arguments = new Dictionary<string, object?> { ["key"] = dedupeKey };
        if (hours is { } h)
        {
            arguments["hours"] = h;
        }

        var result = await RunAsync("hub.anomaly.snooze", arguments, InvocationSource.ChatButton, component.User)
            .ConfigureAwait(false);

        if (!IsFailure(result))
        {
            await RefreshAnomalyMessageAsync(dedupeKey).ConfigureAwait(false);
        }

        await component.RespondAsync(Render(result), ephemeral: true).ConfigureAwait(false);
    }

    private async Task RefreshAnomalyMessageAsync(string dedupeKey)
    {
        if (ResolveDashboardChannel() is not { } channel)
        {
            return;
        }

        var anomaly = anomalies.All.FirstOrDefault(a => a.DedupeKey == dedupeKey);
        if (anomaly is null)
        {
            return;
        }

        var content = DiscordAnomalyRenderer.Render(anomaly);
        var components = anomaly.State == AnomalyState.Open
            ? BuildSnoozeButtons(dedupeKey)
            : new ComponentBuilder().Build();

        await PostOrEditAnomalyMessageAsync(channel, dedupeKey, content, components).ConfigureAwait(false);
    }

    private async Task<CapabilityResult> RunAsync(string capabilityKey,
        IReadOnlyDictionary<string, object?> arguments, InvocationSource source, SocketUser user)
    {
        var invocation = new CapabilityInvocation(
            CapabilityKey: capabilityKey,
            Arguments: arguments,
            Source: source,
            ActorId: $"discord:{user.Id}",
            IsAdministrator: IsAdministrator(user));

        return await executor.ExecuteAsync(invocation, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Verdict tranché ici, transmis au noyau qui seul l'applique (ADR-0004, ADR-0016) : le
    /// noyau ne connaît aucun rôle Discord, et cet adaptateur ne connaît aucune règle
    /// d'autorisation autre que « ce rôle-là ».
    /// </summary>
    private bool IsAdministrator(SocketUser user)
    {
        var roleId = config.GetValue(HubSettings.DiscordAdminRoleIdKey);

        return user is SocketGuildUser guildUser
               && ulong.TryParse(roleId, out var role)
               && guildUser.Roles.Any(r => r.Id == role);
    }

    private static bool IsFailure(CapabilityResult result) => result.Outcome == CapabilityOutcome.Failed;

    private static string Render(CapabilityResult result) =>
        result.Message ?? (result.Outcome == CapabilityOutcome.Ok ? "Fait." : "Opération transmise.");

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Le journal Discord.Net ne doit jamais interrompre la passerelle.")]
    private Task OnDiscordLog(LogMessage message)
    {
        try
        {
            var level = message.Severity switch
            {
                LogSeverity.Critical or LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                _ => LogLevel.Debug,
            };

#pragma warning disable CA2254
            logger.Log(level, message.Exception, "[Discord.Net] {Message}", message.Message);
#pragma warning restore CA2254
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Journal Discord.Net illisible.");
        }

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }
}
