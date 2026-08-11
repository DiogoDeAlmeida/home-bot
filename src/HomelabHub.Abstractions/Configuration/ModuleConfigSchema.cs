namespace HomelabHub.Abstractions.Configuration;

/// <summary>
/// Description de la configuration d'un module, construite de façon fluide.
/// </summary>
/// <remarks>
/// C'est la pièce qui rend le système extensible : le front React génère le formulaire à
/// partir de ce schéma, sans qu'une ligne de TypeScript soit écrite pour un nouveau module.
/// Le même schéma sert de validateur côté serveur et désigne les champs à chiffrer.
/// </remarks>
public sealed class ModuleConfigSchema
{
    private readonly List<ConfigField> _fields = [];

    /// <summary>Champs déclarés, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<ConfigField> Fields => _fields;

    /// <summary>Texte libre sur une ligne.</summary>
    public ModuleConfigSchema AddText(string key, string label, bool required = false,
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
    public ModuleConfigSchema AddUrl(string key, string label, bool required = false,
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
    public ModuleConfigSchema AddSecret(string key, string label, bool required = false,
                                        string? help = null) =>
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
    public ModuleConfigSchema AddBool(string key, string label, bool defaultValue = false,
                                      string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Boolean,
            DefaultValue = defaultValue,
            Help = help,
        });

    /// <summary>Entier.</summary>
    public ModuleConfigSchema AddInt(string key, string label, int defaultValue = 0,
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

    /// <summary>Durée — intervalle de polling, seuil de détection d'anomalie, rétention.</summary>
    public ModuleConfigSchema AddDuration(string key, string label, TimeSpan defaultValue,
                                          string? help = null) =>
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
    public ModuleConfigSchema AddSelect(string key, string label,
                                        IReadOnlyList<ConfigOption>? options = null,
                                        string? optionsFrom = null,
                                        IReadOnlyList<string>? dependsOn = null,
                                        bool required = false, string? help = null) =>
        Add(new ConfigField
        {
            Key = key,
            Label = label,
            Kind = ConfigFieldKind.Select,
            Required = required,
            Options = options,
            OptionsFrom = optionsFrom,
            DependsOn = dependsOn,
            Help = help,
        });

    /// <summary>Choix multiple. Mêmes règles que <see cref="AddSelect"/>.</summary>
    public ModuleConfigSchema AddMultiSelect(string key, string label,
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

    private ModuleConfigSchema Add(ConfigField field)
    {
        if (_fields.Exists(f => string.Equals(f.Key, field.Key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Champ de configuration en double : « {field.Key} ».");
        }

        _fields.Add(field);
        return this;
    }
}
