// GameCore.Studio.Etos - marshals work from network threads onto the Unity main thread. Events and answers arrive on
// thread-pool threads; Studio state (requests, staging, the change-set engine, AssetDatabase) is touched only from the
// queue's pump, which the session drives from EditorApplication.update (tests pump it themselves).
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;

namespace GameCore.Studio.Etos
{
    /// <summary>A work queue drained on the thread that created it.</summary>
    public sealed class MainThreadQueue
    {
        private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();
        private readonly IStudioLog? _log;

        public MainThreadQueue(IStudioLog? log = null)
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _log = log;
        }

        /// <summary>The managed id of the pumping thread.</summary>
        public int MainThreadId { get; }

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == MainThreadId;

        /// <summary>Items waiting.</summary>
        public int Pending => _work.Count;

        /// <summary>Runs <paramref name="action"/> on the next pump.</summary>
        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _work.Enqueue(action);
        }

        /// <summary>Runs <paramref name="work"/> on the main thread and completes with its result.</summary>
        public Task<T> Run<T>(Func<T> work)
        {
            TaskCompletionSource<T> done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try
                {
                    done.TrySetResult(work());
                }
                catch (Exception error)
                {
                    done.TrySetException(error);
                }
            });
            return done.Task;
        }

        public async Task Run(Action work, CancellationToken token)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => done.TrySetCanceled()))
            {
                Post(() =>
                {
                    if (token.IsCancellationRequested) { done.TrySetCanceled(); return; }
                    try { work(); done.TrySetResult(true); }
                    catch (Exception error) { done.TrySetException(new Exception(new SecretRedactor().Redact(error.Message))); }
                });
                await done.Task.ConfigureAwait(false);
            }
            return;
        }

        /// <summary>Drains up to <paramref name="max"/> items on the calling (main) thread; returns how many ran.</summary>
        public int Pump(int max = 256)
        {
            int ran = 0;
            while (ran < max && _work.TryDequeue(out Action? action))
            {
                ran++;
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    _log?.Write(StudioLogLevel.Error, "etos", new SecretRedactor().Redact("a main-thread callback failed: " + error.Message));
                }
            }

            return ran;
        }
    }
}
