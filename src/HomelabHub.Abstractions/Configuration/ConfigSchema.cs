namespace HomelabHub.Abstractions.Configuration;

/// <summary>
/// Primitive de description d'une configuration, construite de façon fluide.
/// </summary>
/// <remarks>
/// <para>
/// C'est la pièce qui rend le système extensible : le front React génère le formulaire à partir
/// de ces champs, sans qu'une ligne de TypeScript soit écrite pour un nouveau module. Le même
/// schéma sert de validateur côté serveur et désigne les champs à chiffrer.
/// </para>
/// <para>
/// La primitive est <b>partagée entre les modules et le hub lui-même</b> (ADR-0013) : le noyau a
/// aussi des réglages — rétention des sauvegardes, niveau de journalisation — et rien ne
/// justifiait d'écrire un second générateur de formulaire pour eux. Aux yeux de l'interface, le
/// noyau est un pseudo-module ; dans le contrat, il n'en est pas un.
/// </para>
/// <para>
/// Le paramètre de type sert uniquement à préserver le chaînage fluide dans les classes
/// dérivées : <c>new ModuleConfigSchema().AddUrl(…).AddSecret(…)</c> reste typé
/// <c>ModuleConfigSchema</c>.
/// </para>
/// </remarks>
public abstract class ConfigSchema<TSelf> where TSelf : ConfigSchema<TSelf>
{
    private readonly List<ConfigField> _fields = [];

    /// <summary>Champs déclarés, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<ConfigField> Fields => _fields;

    /// <summary>Texte libre sur une ligne.</summary>
    public TSelf AddText(string key, string label, bool required = false,
                         string? help = null, string? defaultValue = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Text,
            Required = required,
            Help = help,
            DefaultValue = defaultValue,
        });

    /// <summary>URL d'un service, validée côté serveur.</summary>
    public TSelf AddUrl(string key, string label, bool required = false,
                        string? help = null, string? defaultValue = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Url,
            Required = required,
            Help = help,
            DefaultValue = defaultValue,
        });

    /// <summary>Secret : chiffré au repos, masqué en lecture, absent des journaux.</summary>
    public TSelf AddSecret(string key, string label, bool required = false, string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Secret,
            Required = required,
            Secret = true,
            Help = help,
        });

    /// <summary>Case à cocher.</summary>
    public TSelf AddBool(string key, string label, bool defaultValue = false, string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Boolean,
            DefaultValue = defaultValue,
            Help = help,
        });

    /// <summary>Entier.</summary>
    public TSelf AddInt(string key, string label, int defaultValue = 0,
                        bool required = false, string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Integer,
            Required = required,
            DefaultValue = defaultValue,
            Help = help,
        });

    /// <summary>Durée — intervalle de polling, seuil de détection, rétention.</summary>
    public TSelf AddDuration(string key, string label, TimeSpan defaultValue, string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Duration,
            DefaultValue = defaultValue,
            Help = help,
        });

    /// <summary>
    /// Choix unique. Fournir soit <paramref name="options"/> (liste figée), soit
    /// <paramref name="optionsFrom"/> (résolution à l'exécution — non implémentée en v1,
    /// voir <see cref="ConfigField.OptionsFrom"/>).
    /// </summary>
    public TSelf AddSelect(string key, string label,
                           IReadOnlyList<ConfigOption>? options = null,
                           string? optionsFrom = null,
                           IReadOnlyList<string>? dependsOn = null,
                           bool required = false, string? help = null,
                           string? defaultValue = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Select,
            Required = required,
            Options = options,
            OptionsFrom = optionsFrom,
            DependsOn = dependsOn,
            DefaultValue = defaultValue,
            Help = help,
        });

    /// <summary>Choix multiple. Mêmes règles que <see cref="AddSelect"/>.</summary>
    public TSelf AddMultiSelect(string key, string label,
                                IReadOnlyList<ConfigOption>? options = null,
                                string? optionsFrom = null,
                                IReadOnlyList<string>? dependsOn = null,
                                string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.MultiSelect,
            Options = options,
            OptionsFrom = optionsFrom,
            DependsOn = dependsOn,
            Help = help,
        });

    private TSelf Add(ConfigField field)
    {
        if (_fields.Exists(f => string.Equals(f.Key, field.Key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Champ de configuration en double : « {field.Key} ».");
        }

        _fields.Add(field);
        return (TSelf)this;
    }
}

/// <summary>Configuration d'un module. Les clés sont préfixées par la clé du module.</summary>
public sealed class ModuleConfigSchema : ConfigSchema<ModuleConfigSchema>;

/// <summary>
/// Réglages du hub lui-même. Les clés sont préfixées par <c>hub.</c>, préfixe réservé qu'aucun
/// module ne peut revendiquer — le validateur de clés de module le refuse.
/// </summary>
public sealed class HubConfigSchema : ConfigSchema<HubConfigSchema>;
