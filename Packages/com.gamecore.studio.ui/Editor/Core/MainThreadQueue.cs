// GameCore.Studio.UI - marshals gateway callbacks (which may arrive on any thread, 04 s2) onto the editor main thread.
// One queue per UI context; drained on EditorApplication.update, or explicitly by tests.
#nullable enable
using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>A queue of actions run on the editor main thread.</summary>
    public sealed class MainThreadQueue : IDisposable
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private bool _hooked;

        /// <param name="hookEditorUpdate">Drain on every EditorApplication.update (tests pass false and call <see cref="Drain"/>).</param>
        public MainThreadQueue(bool hookEditorUpdate)
        {
            if (hookEditorUpdate)
            {
                EditorApplication.update += OnUpdate;
                _hooked = true;
            }
        }

        public int Pending => _queue.Count;

        /// <summary>Queues an action (any thread).</summary>
        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _queue.Enqueue(action);
        }

        /// <summary>Runs every queued action (main thread); returns how many ran.</summary>
        public int Drain()
        {
            int ran = 0;
            while (_queue.TryDequeue(out Action? action))
            {
                ran++;
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }

            return ran;
        }

        public void Dispose()
        {
            if (_hooked)
            {
                EditorApplication.update -= OnUpdate;
                _hooked = false;
            }
        }

        private void OnUpdate() => Drain();
    }
}
