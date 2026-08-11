namespace HomelabHub.Host.Auth;

/// <summary>
/// Verrouille l'API tant que l'assistant de premier démarrage n'a pas défini de mot de passe.
/// </summary>
/// <remarks>
/// <para>
/// « Refuser de démarrer sans mot de passe configuré » est irréalisable tel quel : le mot de
/// passe se définit <i>via</i> l'interface. La bonne formulation est un <b>mode d'installation
/// verrouillé</b> — l'API répond 503, sauf les routes de l'assistant.
/// </para>
/// <para>
/// Le verrou ne porte que sur <c>/api</c>. Les fichiers statiques et le repli SPA passent
/// toujours : c'est l'interface elle-même qui affiche l'écran d'installation, et la verrouiller
/// reviendrait à ne jamais pouvoir sortir du mode installation.
/// </para>
/// <para>
/// Les webhooks vivent sous <c>/api/webhooks</c> : ils sont donc couverts par le refus,
/// conformément à ADR-0012. Rien ne doit pouvoir écrire dans le hub avant qu'il ait un
/// propriétaire.
/// </para>
/// </remarks>
internal sealed class SetupGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdminAccount admin)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(admin);

        if (admin.IsConfigured || !IsGuarded(context.Request.Path))
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

    private static bool IsGuarded(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/setup", StringComparison.OrdinalIgnoreCase);
}
