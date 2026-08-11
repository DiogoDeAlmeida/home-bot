# Ajouter un module

> **Ce document est un test.** S'il dépasse deux pages, l'abstraction est ratée
> ([ADR-0010](adr/0010-frontieres-de-projets.md)).

## 1. Le projet

```
src/HomelabHub.Modules.<Domaine>/
```

Une seule référence, et le test d'architecture le vérifie en CI :

```xml
<ProjectReference Include="..\HomelabHub.Abstractions\HomelabHub.Abstractions.csproj" />
```

Si l'abstraction ne suffit pas pour ce que tu veux faire, c'est **l'abstraction qu'il faut
corriger** — pas cette liste qu'il faut allonger. Ouvre un ADR.

> Ne nomme pas l'espace de noms `HomelabHub.Modules.System` : à l'intérieur, C# résout `System.IO`
> en `HomelabHub.Modules.System.IO`. Le module système utilise `…Modules.SystemInfo`.

## 2. La déclaration

```csharp
public sealed class MediaModule : IHubModule
{
    public string Key => "media";                    // préfixe TOUT : config, routes, /media …
    public string DisplayName => "Média";
    public string Description => "Requêtes, téléchargements et imports.";

    public ModuleConfigSchema ConfigSchema => new ModuleConfigSchema()
        .AddUrl("radarr.url", "URL Radarr", required: true)
        .AddSecret("radarr.apiKey", "Clé API Radarr", required: true);

    public void Register(IModuleRegistrationContext context) => context
        .AddState(MediaSnapshot.Empty)
        .AddServiceClient<IRadarrClient, RadarrClient>("radarr")
        .AddPoller<MediaPoller>(TimeSpan.FromMinutes(1), "pollIntervalSeconds")
        .AddWebhook<RadarrWebhookHandler>("radarr")
        .AddHealthCheck<MediaHealthCheck>()
        .AddCapability<QueueListCapability>()
        .AddWidget<QueueWidget>();
}
```

Puis, dans `Program.cs` :

```csharp
builder.Services.AddHubCore(new SystemModule(), new MediaModule());
```

C'est la seule ligne du noyau qui change, et c'est une liste.

**Contrainte d'instanciation :** le module est construit *avant* le conteneur d'injection de
dépendances. Aucune dépendance injectée, aucun état, aucun appel réseau dans cette classe.

## 3. Ce que le noyau fournit

| Injectable | Rôle |
|---|---|
| `IModuleState<TSnapshot>` | Snapshot immuable, échange atomique ([ADR-0009](adr/0009-concurrence-du-snapshot.md)) |
| `IModuleConfiguration<TModule>` | Config du module, clés relatives, valeurs par défaut du schéma |
| `IEventPublisher` | Publication d'événements et d'anomalies |
| `IHubPlatform` | Version, démarrage, répertoires |
| `IBackupRequester<TModule>` | **Demander** une sauvegarde. Le noyau décide, applique l'anti-rebond et journalise l'appelant ([ADR-0014](adr/0014-demander-une-sauvegarde-nest-pas-la-piloter.md)) |
| Le client typé déclaré par `AddServiceClient` | Délai d'attente, URL et authentification depuis la config |

Et ce dont tu n'as **pas** à t'occuper : activation, cadence des pollers, route et jeton de
webhook, reconnexion d'une connexion longue durée, autorisation, formulaire de configuration,
chiffrement des secrets, diffusion des changements.

## 4. Les trois modes d'ingestion

Ils diffèrent par **qui pilote le cycle de vie**, et convergent tous vers le même snapshot
([ADR-0003](adr/0003-trois-modes-ingestion.md)).

```csharp
// Le noyau appelle, périodiquement. Source de vérité.
public Task PollAsync(CancellationToken ct);

// Le noyau route POST /api/webhooks/{module}/{hook}, déjà authentifié.
// Le payload dit qu'il s'est passé quelque chose ; il ne contient pas l'état.
public Task<WebhookResult> HandleAsync(WebhookRequest r, CancellationToken ct)
    => Task.FromResult(WebhookResult.AcceptedAndRefresh());   // ← déclenche un poll anticipé

// Le module tient la boucle, le noyau tient la politique (backoff, santé, activation).
public async Task RunAsync(IConnectionContext ctx, CancellationToken ct)
{
    ctx.ReportConnected();                                     // ← remet le backoff à zéro
    await foreach (var change in ReadAsync(ct)) { state.Mutate(s => s.With(change)); }
}
```

## 5. Les deux règles qui se paient cher si on les oublie

**La transformation passée à `Mutate` doit être pure.** Elle est rejouée en cas de contention :
pas d'écriture en base, pas de publication d'événement, pas de compteur incrémenté. Les effets
de bord viennent après, sur le snapshot renvoyé. Renvoyer l'instance reçue signifie « rien de
neuf » et n'émet aucune notification.

**Un détecteur est une projection sans état, jamais un émetteur.** À chaque cycle il republie
*l'ensemble* de ce qui va mal, en repartant du snapshot. Ce qui disparaît est résolu par le
noyau ([ADR-0005](adr/0005-anomalies-comme-etats.md)). Ne tiens aucune table d'anomalies : tu
dupliquerais le noyau et finirais par diverger de lui.

## 6. Exposer une capacité

```csharp
public CapabilityDescriptor Descriptor { get; } = new(
    Key: "media.queue.pause",              // toujours préfixé par la clé du module
    DisplayName: "Mettre en pause",
    Description: "Suspend un téléchargement.",     // ≤ 100 car. si exposée comme commande
    Parameters: [new CapabilityParameter("hash", "Torrent", CapabilityParameterType.String, Required: true)],
    Kind: CapabilityKind.Mutation,         // QUI peut appeler : Mutation ⇒ administrateurs
    Exposure: CapabilityExposure.All,      // D'OÙ : All, ou Api seul pour ce qui est sensible
    Command: new CommandBinding("queue", "pause"),
    RequireConfirmation: true);            // propriété de l'opération, pas du canal
```

`Kind` et `Exposure` sont indépendants et répondent à deux questions différentes. Une capacité
qui manipule des secrets reste en `CapabilityExposure.Api` — `system.backup.create` produit une
archive contenant le keyring, elle n'ira sur aucun canal conversationnel.

**Le chemin de commande est neutre** ([ADR-0016](adr/0016-extensibilite-des-adaptateurs.md)) :
`["queue", "pause"]` est relatif au module, et chaque adaptateur l'épelle à sa façon —
`/media queue pause` côté Discord. Un seul segment suffit pour une commande simple
(`new CommandBinding("disk")`). Ne nomme jamais une plateforme dans une capacité.

**Le validateur de démarrage refuse** : une clé mal préfixée, une commande contredisant
l'`Exposure`, un chemin vide ou trop profond, une description trop longue, un paramètre
obligatoire après un optionnel, un segment en majuscules, un doublon. L'échec est bruyant et
immédiat, jamais silencieux.

Ne vérifie pas les droits dans `ExecuteAsync` : l'autorisation est tranchée par le noyau, pour
les commandes, les boutons et l'API à la fois ([ADR-0004](adr/0004-autorisation-cote-noyau.md)).

## 7. Le formulaire

Rien à écrire côté React : il est généré depuis `ConfigSchema`. Les champs `Secret` sont chiffrés
au repos et ne repartent jamais en clair de l'API.

`OptionsFrom` — options résolues à l'exécution — existe dans le contrat mais **n'est pas encore
résolu par le front** ([ADR-0011](adr/0011-options-dynamiques-differees.md)) : une saisie libre
s'affiche à la place.

## 8. Tester

Écris les cas limites **avant** le code, comme
`tests/HomelabHub.Modules.Media.Tests/CorrelationCases.cs`. Ce sont eux qui déterminent la forme
du modèle de données, et ils coûtent bien moins cher à écrire qu'à découvrir.
