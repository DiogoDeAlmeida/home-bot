using System.Collections.Concurrent;
using HomelabHub.Abstractions.Modules;
using Microsoft.Extensions.Logging;

namespace HomelabHub.Core.Modules;

/// <summary>
/// Implémentation d'<see cref="IModuleState{TSnapshot}"/> : échange atomique sans verrou
/// (ADR-0009).
/// </summary>
internal sealed class ModuleState<TSnapshot>(TSnapshot initial, ILogger<ModuleState<TSnapshot>> logger)
    : IModuleState<TSnapshot>
    where TSnapshot : class
{
    private readonly ConcurrentDictionary<Guid, Action<TSnapshot>> _subscribers = new();
    private TSnapshot _current = initial ?? throw new ArgumentNullException(nameof(initial));

    public TSnapshot Current => Volatile.Read(ref _current);

    public TSnapshot Mutate(Func<TSnapshot, TSnapshot> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        while (true)
        {
            var previous = Volatile.Read(ref _current);
            var next = update(previous)
                ?? throw new InvalidOperationException(
                    "Une transformation de snapshot ne peut pas renvoyer null.");

            // Renvoyer l'instance reçue est le moyen explicite de dire « rien de neuf » :
            // aucune notification, donc aucune trame SignalR ni aucune édition du message
            // Discord. C'est ce qui évite de rafraîchir le dashboard pour rien.
            if (ReferenceEquals(next, previous))
            {
                return previous;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, previous), previous))
            {
                Notify(next);
                return next;
            }

            // Un autre écrivain a gagné la course : on rejoue la transformation sur son
            // résultat. C'est précisément pourquoi elle doit être pure.
        }
    }

    public IDisposable Subscribe(Action<TSnapshot> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        var token = Guid.NewGuid();
        _subscribers[token] = onChanged;
        return new Subscription(this, token);
    }

    private void Notify(TSnapshot snapshot)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                subscriber(snapshot);
            }
            catch (Exception ex)
            {
                // Un abonné défaillant ne doit pas empêcher les autres d'être notifiés, ni
                // remonter dans le chemin d'écriture d'une source d'ingestion.
                logger.LogError(ex, "Un abonné au snapshot {Snapshot} a levé une exception.",
                    typeof(TSnapshot).Name);
            }
        }
    }

    private sealed class Subscription(ModuleState<TSnapshot> state, Guid token) : IDisposable
    {
        public void Dispose() => state._subscribers.TryRemove(token, out _);
    }
}
