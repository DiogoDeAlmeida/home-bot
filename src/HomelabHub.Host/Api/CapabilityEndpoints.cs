using System.Text.Json;
using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Abstractions.Events;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Events;

namespace HomelabHub.Host.Api;

/// <summary>
/// Exposition REST des capacités.
/// </summary>
/// <remarks>
/// C'est une API de type RPC — <c>POST /api/capabilities/media.queue.pause</c> — et non une API
/// REST au sens strict. C'est assumé : les capacités sont des opérations nommées, pas des
/// ressources. Prétendre l'inverse produirait des URL contorsionnées sans rien apporter.
/// </remarks>
internal static class CapabilityEndpoints
{
    public static void MapCapabilities(this IEndpointRouteBuilder app)
    {
        var capabilities = app.MapGroup("/api/capabilities").RequireAuthorization();

        capabilities.MapGet("/", (ICapabilityRegistry registry) => Results.Ok(
            registry.All
                .Where(c => c.Descriptor.Exposure.HasFlag(CapabilityExposure.Api))
                .Select(c => new
                {
                    c.ModuleKey,
                    c.Descriptor.Key,
                    c.Descriptor.DisplayName,
                    c.Descriptor.Description,
                    kind = c.Descriptor.Kind.ToString(),
                    exposure = c.Descriptor.Exposure.ToString(),
                    c.Descriptor.RequireConfirmation,
                    // Chemin neutre : chaque adaptateur l'épelle dans sa syntaxe (ADR-0016).
                    // L'interface web l'affiche à titre indicatif.
                    command = c.Descriptor.Command is null
                        ? null
                        : string.Join(' ', new[] { c.ModuleKey }.Concat(c.Descriptor.Command.Path)),
                    parameters = c.Descriptor.Parameters.Select(p => new
                    {
                        p.Name,
                        p.Description,
                        type = p.Type.ToString(),
                        p.Required,
                        p.DefaultValue,
                        p.Choices,
                    }),
                })));

        capabilities.MapPost("/{key}", async (string key, JsonElement? body,
                                              ICapabilityExecutor executor,
                                              CancellationToken cancellationToken) =>
        {
            var arguments = ReadArguments(body);

            // L'interface web n'a qu'un compte, et il est administrateur : l'autorisation est
            // tranchée par le noyau, qui reçoit ici une identité déjà vérifiée par le cookie.
            var invocation = new CapabilityInvocation(
                CapabilityKey: key,
                Arguments: arguments,
                Source: InvocationSource.Api,
                ActorId: "web:admin",
                IsAdministrator: true);

            var result = await executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                CapabilityOutcome.Failed => Results.BadRequest(new { result.Outcome, result.Message }),
                _ => Results.Ok(new { result.Outcome, result.Message, result.Payload }),
            };
        });

        app.MapGet("/api/journal", (IHubJournal journal, int? count, HubEventSeverity? minimum) =>
                Results.Ok(journal.Recent(count ?? 100, minimum)))
           .RequireAuthorization();
    }

    private static Dictionary<string, object?> ReadArguments(JsonElement? body)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (body is not { ValueKind: JsonValueKind.Object } element)
        {
            return arguments;
        }

        foreach (var property in element.EnumerateObject())
        {
            arguments[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        return arguments;
    }
}
