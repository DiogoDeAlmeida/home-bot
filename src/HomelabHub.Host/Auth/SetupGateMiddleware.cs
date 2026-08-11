namespace HomelabHub.Host.Auth;

/// <summary>
/// Verrouille le hub tant que l'assistant de premier démarrage n'a pas défini de mot de passe.
/// </summary>
/// <remarks>
/// <para>
/// « Refuser de démarrer sans mot de passe configuré » est irréalisable tel quel : le mot de
/// passe se définit <i>via</i> l'interface. La bonne formulation est un <b>mode d'installation
/// verrouillé</b> — seules la sonde de disponibilité et les routes de l'assistant répondent,
/// tout le reste renvoie 503.
/// </para>
/// <para>
/// Les webhooks sont couverts par ce refus, conformément à ADR-0012 : un jeton de module n'a pas
/// encore été généré, et rien ne doit pouvoir écrire dans le hub avant qu'il ait un propriétaire.
/// </para>
/// </remarks>
internal sealed class SetupGateMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedPrefixes =
    [
        "/healthz",
        "/api/setup",
    ];

    public async Task InvokeAsync(HttpContext context, AdminAccount admin)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(admin);

        if (admin.IsConfigured || IsAllowed(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "setup_required",
            message = "Le hub n'est pas encore initialisé. Définir un mot de passe administrateur "
                      + "via POST /api/setup avant toute autre opération.",
        }).ConfigureAwait(false);
    }

    private static bool IsAllowed(PathString path) =>
        Array.Exists(AllowedPrefixes,
            prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
