#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;

namespace GameCore.Studio.Views
{
    /// <summary>
    /// Reflection-free soft binding for P2.1's singleton-owned SelectionModel. The UI supplies its model's
    /// Targets, Set and Changed members, and its viewport focus action, without a package dependency cycle.
    /// Rebind after the UI recreates its context; dispose the old binding to detach Changed.
    /// </summary>
    public sealed class StudioSelectionAdapter : IStudioSelectionBridge, IDisposable
    {
        private readonly Func<IReadOnlyList<AuthoringRef>> _current;
        private readonly Action<IReadOnlyList<AuthoringRef>> _select;
        private readonly Action<AuthoringRef> _focus;
        private readonly Action<Action> _unsubscribe;
        private bool _disposed;

        public StudioSelectionAdapter(Func<IReadOnlyList<AuthoringRef>> current,
            Action<IReadOnlyList<AuthoringRef>> select, Action<AuthoringRef> focus,
            Action<Action> subscribe, Action<Action> unsubscribe)
        {
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _select = select ?? throw new ArgumentNullException(nameof(select));
            _focus = focus ?? throw new ArgumentNullException(nameof(focus));
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
            (subscribe ?? throw new ArgumentNullException(nameof(subscribe)))(OnChanged);
        }

        public IReadOnlyList<AuthoringRef> Current => _current();
        public event Action? SelectionChanged;
        public void Select(IReadOnlyList<AuthoringRef> targets) => _select(targets);
        public void Focus(AuthoringRef target) { Select(new[] { target }); _focus(target); }
        private void OnChanged() => SelectionChanged?.Invoke();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _unsubscribe(OnChanged);
        }
    }
}
