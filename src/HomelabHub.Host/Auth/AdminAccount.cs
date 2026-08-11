using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HomelabHub.Core.Configuration;

namespace HomelabHub.Host.Auth;

/// <summary>
/// Le compte administrateur unique de l'interface web.
/// </summary>
/// <remarks>
/// <para>
/// Un seul compte, sans gestion d'utilisateurs : l'interface concentre toutes les clés d'API du
/// homelab et n'est utilisée que par une personne. Discord, lui, sert le foyer — mais Discord a
/// sa propre identité et son propre contrôle de rôle (ADR-0004).
/// </para>
/// <para>
/// PBKDF2-SHA256 à 210 000 itérations, sel de 16 octets, comparaison en temps constant. Écrit à
/// la main plutôt que via <c>Microsoft.Extensions.Identity.Core</c> : trente lignes contre un
/// paquet et son modèle d'utilisateur dont rien d'autre ne servirait.
/// </para>
/// </remarks>
public sealed class AdminAccount(IHubConfigStore store)
{
    private const string HashKey = "hub.admin.passwordHash";
    private const string CreatedKey = "hub.admin.createdAt";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>Longueur minimale imposée à l'assistant de premier démarrage.</summary>
    public const int MinimumPasswordLength = 10;

    /// <summary>Le hub est-il sorti du mode d'installation ?</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(store.GetValue(HashKey));

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException(
                $"Le mot de passe doit faire au moins {MinimumPasswordLength} caractères.",
                nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);

        var encoded = string.Create(CultureInfo.InvariantCulture,
            $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}");

        await store.SetManyAsync(new Dictionary<string, ConfigValue>
        {
            [HashKey] = new(encoded, Secret: true),
            [CreatedKey] = new(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), Secret: false),
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool Verify(string password)
    {
        var encoded = store.GetValue(HashKey);
        if (string.IsNullOrWhiteSpace(encoded) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = encoded.Split('.');
        if (parts.Length != 4
            || parts[0] != "v1"
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
