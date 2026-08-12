using HomelabHub.Abstractions.Capabilities;
using HomelabHub.Core.Capabilities;
using HomelabHub.Core.Configuration;

namespace HomelabHub.Core.Anomalies;

/// <summary>
/// Tait une anomalie, pendant une durée donnée ou jusqu'à sa résolution.
/// </summary>
/// <remarks>
/// <para>
/// Capacité du noyau, pas d'un module : c'est <see cref="AnomalyEngine"/> qui possède la table,
/// et aucun domaine ne peut prétendre à cette opération plus qu'un autre. Elle s'inscrit donc
/// sous <see cref="HubSettings.Prefix"/>, exactement comme les réglages du hub le font déjà pour
/// la configuration (ADR-0013), et traverse le même <see cref="ICapabilityExecutor"/> que toute
/// autre mutation — même autorisation, même journal d'audit, même exposition REST et
/// conversationnelle automatique.
/// </para>
/// <para>
/// <b>Sans confirmation, délibérément.</b> Contrairement à un import manuel ou une écriture
/// qBittorrent, cette opération ne touche aucun service externe : au pire, elle retarde une
/// notification. C'est aussi l'usage visé au cadrage — un bouton à un clic sur le tableau de
/// bord, pas une modale de plus pour un geste que la faible tolérance au bruit du foyer rend
/// fréquent.
/// </para>
/// </remarks>
internal sealed class AnomalySnoozeCapability(IAnomalyEngine engine, TimeProvider time) : IHubCapability
{
    public CapabilityDescriptor Descriptor { get; } = new(
        Key: $"{HubSettings.Prefix}.anomaly.snooze",
        DisplayName: "Mettre en sommeil",
        Description: "Tait une anomalie pendant une durée donnée, ou jusqu'à sa résolution.",
        Parameters:
        [
            new CapabilityParameter("key", "Clé de l'anomalie à mettre en sommeil",
                                    CapabilityParameterType.String, Required: true),
            new CapabilityParameter("hours", "Durée en heures — absente ou nulle pour jusqu'à résolution",
                                    CapabilityParameterType.Integer),
        ],
        Kind: CapabilityKind.Mutation,
        Exposure: CapabilityExposure.All,
        Command: new CommandBinding("anomaly", "snooze"));

    public Task<CapabilityResult> ExecuteAsync(CapabilityInvocation invocation,
                                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var dedupeKey = invocation.GetString("key");
        if (string.IsNullOrWhiteSpace(dedupeKey))
        {
            return Task.FromResult(CapabilityResult.Fail("Aucune anomalie indiquée."));
        }

        var now = time.GetUtcNow();

        // Deux formes prévues au cadrage : une échéance, ou « jusqu'à résolution », qui ne se
        // réarme qu'après un passage effectif par l'état résolu. 0 heure n'a pas de sens comme
        // durée : c'est le même sentinel qu'utilisait l'ancien endpoint REST dédié.
        var hours = invocation.GetInteger("hours");
        var until = hours > 0 ? now.AddHours(hours) : (DateTimeOffset?)null;

        var snoozed = engine.Snooze(dedupeKey, until, now);

        // Une durée relative, pas une heure absolue : le noyau raisonne en UTC de bout en bout
        // et n'a nulle part où convertir vers Europe/Paris. « Dans 6 h » reste vrai quel que
        // soit le fuseau qui lit le message ; une heure figée en UTC serait fausse pour
        // quiconque le lit depuis le foyer.
        return Task.FromResult(snoozed
            ? CapabilityResult.Ok(hours > 0
                ? $"Anomalie mise en sommeil pour {hours} h."
                : "Anomalie mise en sommeil jusqu'à sa résolution.")
            : CapabilityResult.Fail("Anomalie inconnue, ou déjà résolue."));
    }
}
