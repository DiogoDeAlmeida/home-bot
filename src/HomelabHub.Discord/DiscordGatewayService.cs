using System.Diagnostics.CodeAnalysis;
using global::Discord;
using global::Discord.WebSocket;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;
using HomelabHub.Discord.Commands;
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
/// capacité n'est vérifiée deux fois.
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
    ILogger<DiscordGatewayService> logger) : BackgroundService
{
    private DiscordSocketClient? _client;
    private DiscordCommandPlan _plan = new([], new Dictionary<string, string>());

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

        try
        {
            await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
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
            var privateReply = registered?.Descriptor.Command?.PrivateReply ?? false;

            var invocation = new CapabilityInvocation(
                CapabilityKey: capabilityKey,
                Arguments: arguments,
                Source: InvocationSource.ChatCommand,
                ActorId: $"discord:{command.User.Id}",
                IsAdministrator: IsAdministrator(command.User));

            var result = await executor.ExecuteAsync(invocation, CancellationToken.None).ConfigureAwait(false);

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
