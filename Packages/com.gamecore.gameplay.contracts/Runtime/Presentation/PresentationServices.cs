// GameCore.Gameplay.Contracts - PresentationServices: the presentation composition point of one gameplay world (P1.5).
//
// Gameplay packages present through interfaces (IPromptPresenter, IDialogueView, IJournalView, IInventoryView,
// IVoiceLinePlayer, IFeedbackSink, IUiIntentSink, ...) and never through each other's types. The UI and audio packages
// implement those interfaces and register the implementations here; the player, dialogue, quest and inventory
// presenters look them up here. One registry per world build: WorldBuildOptions.Presentation hands it to the plan, the
// GameplayWorld exposes it, and it survives the root replacement of a restore. Instance state only (no static state):
// one implementation per interface; registering again replaces the previous one and raises Changed.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>A registry of presentation services keyed by interface type.</summary>
    public sealed class PresentationServices
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly List<Type> order = new List<Type>();

        /// <summary>Raised after a service is registered, replaced or removed, with the service interface type.</summary>
        public event Action<Type>? Changed;

        /// <summary>Registered interface types in first-registration order.</summary>
        public IReadOnlyList<Type> Registered => order;

        public int Count => services.Count;

        /// <summary>Registers <paramref name="service"/> as the implementation of <typeparamref name="T"/>.</summary>
        public void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Type key = typeof(T);
            if (!services.ContainsKey(key))
            {
                order.Add(key);
            }

            services[key] = service;
            Changed?.Invoke(key);
        }

        /// <summary>Removes the implementation of <typeparamref name="T"/> when it is <paramref name="service"/>.</summary>
        public bool Unregister<T>(T service) where T : class
        {
            Type key = typeof(T);
            if (!services.TryGetValue(key, out object? current) || !ReferenceEquals(current, service))
            {
                return false;
            }

            services.Remove(key);
            order.Remove(key);
            Changed?.Invoke(key);
            return true;
        }

        public bool TryGet<T>(out T? service) where T : class
        {
            if (services.TryGetValue(typeof(T), out object? found) && found is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>The implementation of <typeparamref name="T"/>, or null when none is registered.</summary>
        public T? Get<T>() where T : class => TryGet(out T? service) ? service : null;

        public bool Has<T>() where T : class => services.ContainsKey(typeof(T));
    }
}
