// GameCore.Studio.Edit - the single-writer apply queue (docs/studio/03-authoring-contracts.md s7: one apply queue per
// project; applies are serialized and short; long tasks hold no lock). Work items run one at a time on the editor
// update loop (main thread); Drain runs everything queued now (tests, batch mode).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    /// <summary>Serializes applies of one runtime.</summary>
    public sealed class ApplyQueue : IDisposable
    {
        private readonly Queue<Item> _pending = new Queue<Item>();
        private readonly Func<bool> _busy;
        private bool _pumping;
        private bool _hooked;

        internal ApplyQueue(Func<bool> busy)
        {
            _busy = busy ?? throw new ArgumentNullException(nameof(busy));
        }

        /// <summary>Queued items not yet started.</summary>
        public int Pending => _pending.Count;

        /// <summary>Total items completed (tests).</summary>
        public int Completed { get; private set; }

        /// <summary>Queues <paramref name="work"/>; the task completes on the main thread when it has run.</summary>
        public Task<ApplyReport> Enqueue(Func<ApplyReport> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            TaskCompletionSource<ApplyReport> completion = new TaskCompletionSource<ApplyReport>();
            _pending.Enqueue(new Item(work, completion));
            Hook();
            return completion.Task;
        }

        /// <summary>Runs every queued item now, in order; returns how many ran.</summary>
        public int Drain()
        {
            int ran = 0;
            while (_pending.Count > 0 && RunOne())
            {
                ran++;
            }

            return ran;
        }

        public void Dispose()
        {
            Unhook();
            while (_pending.Count > 0)
            {
                _pending.Dequeue().Completion.TrySetCanceled();
            }
        }

        private void Pump()
        {
            RunOne();
            if (_pending.Count == 0)
            {
                Unhook();
            }
        }

        private bool RunOne()
        {
            if (_pumping || _busy() || _pending.Count == 0)
            {
                return false;
            }

            _pumping = true;
            Item item = _pending.Dequeue();
            try
            {
                item.Completion.TrySetResult(item.Work());
            }
            catch (Exception error)
            {
                item.Completion.TrySetException(error);
            }
            finally
            {
                _pumping = false;
                Completed++;
            }

            return true;
        }

        private void Hook()
        {
            if (!_hooked)
            {
                EditorApplication.update += Pump;
                _hooked = true;
            }
        }

        private void Unhook()
        {
            if (_hooked)
            {
                EditorApplication.update -= Pump;
                _hooked = false;
            }
        }

        private readonly struct Item
        {
            public Item(Func<ApplyReport> work, TaskCompletionSource<ApplyReport> completion)
            {
                Work = work;
                Completion = completion;
            }

            public Func<ApplyReport> Work { get; }

            public TaskCompletionSource<ApplyReport> Completion { get; }
        }
    }
}
