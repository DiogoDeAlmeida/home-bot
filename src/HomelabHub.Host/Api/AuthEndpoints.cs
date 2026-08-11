using System.Security.Claims;
using HomelabHub.Host.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HomelabHub.Host.Api;

internal static class AuthEndpoints
{
    public static void MapSetupAndAuth(this IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/setup").AllowAnonymous();

        setup.MapGet("/", (AdminAccount admin) => Results.Ok(new
        {
            configured = admin.IsConfigured,
            minimumPasswordLength = AdminAccount.MinimumPasswordLength,
        }));

        setup.MapPost("/", async (SetPasswordRequest request, AdminAccount admin,
                                 CancellationToken cancellationToken) =>
        {
            // Verrou à usage unique : sans cette vérification, n'importe qui pourrait
            // réinitialiser le mot de passe en appelant la route d'installation.
            if (admin.IsConfigured)
            {
                return Results.Conflict(new { error = "already_configured" });
            }

            if (request.Password is not { Length: >= AdminAccount.MinimumPasswordLength })
            {
                return Results.BadRequest(new
                {
                    error = "password_too_short",
                    message = $"Au moins {AdminAccount.MinimumPasswordLength} caractères.",
                });
            }

            await admin.SetPasswordAsync(request.Password, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { configured = true });
        });

        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        auth.MapPost("/login", async (SetPasswordRequest request, AdminAccount admin,
                                      HttpContext context, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger("HomelabHub.Auth");

            if (request.Password is null || !admin.Verify(request.Password))
            {
                logger.LogWarning("Échec d'authentification depuis {Address}.",
                    context.Connection.RemoteIpAddress);

                return Results.Unauthorized();
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "admin")],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                                      new ClaimsPrincipal(identity)).ConfigureAwait(false);

            return Results.Ok(new { authenticated = true });
        });

        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                         .ConfigureAwait(false);
            return Results.Ok(new { authenticated = false });
        });

        auth.MapGet("/me", (HttpContext context) => Results.Ok(new
        {
            authenticated = context.User.Identity?.IsAuthenticated == true,
            name = context.User.Identity?.Name,
        }));
    }

    internal sealed record SetPasswordRequest(string? Password);
}
