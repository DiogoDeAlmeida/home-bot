using System.Diagnostics.CodeAnalysis;

namespace HomelabHub.Abstractions.Configuration;

/// <summary>Un champ de configuration d'un module.</summary>
public sealed record ConfigField
{
    /// <summary>
    /// Clé relative au module : <c>radarr.apiKey</c> devient <c>media.radarr.apiKey</c> en base.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Libellé du champ, en français.</summary>
    public required string Label { get; init; }

    /// <summary>Nature du champ, qui détermine le composant React et la validation serveur.</summary>
    public required ConfigFieldKind Kind { get; init; }

    /// <summary>Le module refuse de s'activer si un champ obligatoire est vide.</summary>
    public bool Required { get; init; }

    /// <summary>
    /// Chiffré au repos, et jamais renvoyé en clair par l'API : l'écriture seule est la règle,
    /// la lecture renvoie un masque du type <c>••••••1234</c>.
    /// </summary>
    public bool Secret { get; init; }

    /// <summary>Texte d'aide affiché sous le champ.</summary>
    public string? Help { get; init; }

    /// <summary>Valeur retenue tant que rien n'est saisi.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Options figées, pour un <see cref="ConfigFieldKind.Select"/> statique.</summary>
    public IReadOnlyList<ConfigOption>? Options { get; init; }

    /// <summary>
    /// Clé d'une capacité <see cref="Capabilities.CapabilityKind.Query"/> renvoyant la liste
    /// des options à l'exécution — entités Home Assistant, rôles d'un serveur Discord,
    /// dossiers racine Radarr : autant de listes qu'on ne peut pas figer à la compilation.
    /// </summary>
    /// <remarks>
    /// <b>Non résolu en v1 (ADR-0011).</b> Le champ existe pour que le schéma n'ait pas à
    /// changer plus tard, mais le front affiche pour l'instant une saisie libre. Résoudre
    /// dynamiquement suppose un formulaire progressif et des dépendances entre champs, ce qui
    /// ferait gonfler l'étape 1 au-delà du déployable. On l'implémentera quand un module en
    /// aura réellement besoin.
    /// </remarks>
    public string? OptionsFrom { get; init; }

    /// <summary>
    /// Clés dont la valeur doit être renseignée avant que <see cref="OptionsFrom"/> soit
    /// résolvable — on ne peut pas lister les entités Home Assistant avant d'en connaître
    /// l'URL et le jeton. Renseigné dès maintenant, exploité en même temps que
    /// <see cref="OptionsFrom"/>.
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }
}

/// <summary>Une option proposée par un champ à choix.</summary>
public sealed record ConfigOption(string Value, string Label);

/// <remarks>
/// <c>Integer</c> déclenche CA1720. Le nom reste : il désigne le type de saisie attendu côté
/// formulaire, et un synonyme le rendrait moins clair pour l'auteur d'un module.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Le nom décrit le type de saisie du formulaire ; tout synonyme serait moins clair.")]
public enum ConfigFieldKind
{
    /// <summary>Texte libre sur une ligne.</summary>
    Text = 0,

    /// <summary>URL. Validée côté serveur ; le schéma et le port sont vérifiés.</summary>
    Url = 1,

    /// <summary>Secret. Implique <see cref="ConfigField.Secret"/>.</summary>
    Secret = 2,

    /// <summary>Case à cocher.</summary>
    Boolean = 3,

    /// <summary>Entier.</summary>
    Integer = 4,

    /// <summary>Durée, saisie en secondes ou minutes selon l'échelle.</summary>
    Duration = 5,

    /// <summary>Choix unique dans une liste.</summary>
    Select = 6,

    /// <summary>Choix multiple dans une liste.</summary>
    MultiSelect = 7,
}
