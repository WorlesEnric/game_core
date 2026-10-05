// GameCore.Studio.Etos.Client - WS /v1/events with resume (04 s2, SR-4.5, W-ETOS-06). The companion replays its
// durable event ledger after ?after=<cursor> and then streams live events, ordered by cursor. This stream keeps the
// last HANDLED cursor in an ICursorStore (saved after the handler returns), reconnects with exponential backoff and
// jitter after any break (node restart, companion restart, network), and drops frames at or below the cursor, so a
// reconnect replays any unacknowledged event (handlers must be idempotent). A ticket is obtained for every connect (tickets are single use).
#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Connection state of an <see cref="EventStream"/>.</summary>
    public enum EventStreamState
    {
        Stopped,
        Connecting,
        Connected,
        Reconnecting,
    }

    /// <summary>The resumable event stream.</summary>
    public sealed class EventStream : IDisposable
    {
        private readonly CompanionClient _client;
        private readonly ICursorStore _cursors;
        private readonly BackoffPolicy _backoff;
        private readonly object _gate = new object();
        private CancellationTokenSource? _stop;
        private Task? _loop;
        private WebSocket? _socket;
        private long _cursor;
        private int _connects;

        public EventStream(CompanionClient client, ICursorStore cursors, BackoffPolicy? backoff = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
            _backoff = backoff ?? new BackoffPolicy();
            _cursor = cursors.Load();
        }

        /// <summary>Raised on the stream's worker thread for every new frame, in cursor order.</summary>
        public event Action<EventFrame>? Received;

        /// <summary>Completes only after main-thread handling; cancellation abandons unhandled work on reload.</summary>
        public Func<EventFrame, CancellationToken, Task>? HandleAsync { get; set; }

        /// <summary>Raised on the worker thread when the connection state changes (with the error that caused a reconnect).</summary>
        public event Action<EventStreamState, EtosError?>? StateChanged;

        public EventStreamState State { get; private set; } = EventStreamState.Stopped;

        /// <summary>The last handled cursor.</summary>
        public long Cursor => Interlocked.Read(ref _cursor);

        /// <summary>Successful connections so far (1 + reconnects).</summary>
        public int Connects => Volatile.Read(ref _connects);

        /// <summary>How long the last reconnect took from the break to the open socket (ms; 0 before the first break).</summary>
        public double LastReconnectMilliseconds { get; private set; }

        /// <summary>The error that caused the last reconnect.</summary>
        public EtosError? LastError { get; private set; }

        public void Start()
        {
            lock (_gate)
            {
                if (_loop != null)
                {
                    return;
                }

                _stop = new CancellationTokenSource();
                CancellationToken token = _stop.Token;
                _loop = Task.Run(() => RunAsync(token));
            }
        }

        public async Task StopAsync()
        {
            Task? loop;
            lock (_gate)
            {
                loop = _loop;
                _stop?.Cancel();
                _socket?.Abort();
            }

            if (loop != null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_gate)
            {
                _loop = null;
                _stop?.Dispose();
                _stop = null;
            }

            SetState(EventStreamState.Stopped, null);

            return;
        }

        /// <summary>Aborts the current socket (tests and the "force reconnect" action); the stream reconnects by itself.</summary>
        public void ForceDisconnect()
        {
            lock (_gate)
            {
                _socket?.Abort();
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private async Task RunAsync(CancellationToken token)
        {
            int attempt = 0;
            Stopwatch? broken = null;
            while (!token.IsCancellationRequested)
            {
                if (Connects == 0 && attempt == 0)
                {
                    SetState(EventStreamState.Connecting, null);
                }

                WebSocket? socket = null;
                try
                {
                    socket = await _client.ConnectWebSocketAsync("/v1/events", "after=" + Cursor.ToString(CultureInfo.InvariantCulture), token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _socket = socket;
                    }

                    Interlocked.Increment(ref _connects);
                    if (broken != null)
                    {
                        LastReconnectMilliseconds = broken.Elapsed.TotalMilliseconds;
                        _client.Log("event stream reconnected after " + LastReconnectMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms at cursor " + Cursor.ToString(CultureInfo.InvariantCulture));
                        broken = null;
                    }

                    attempt = 0;
                    SetState(EventStreamState.Connected, null);
                    while (!token.IsCancellationRequested)
                    {
                        string? text = await WebSocketText.ReceiveAsync(socket, token).ConfigureAwait(false);
                        if (text == null)
                        {
                            LastError = new EtosError(0, EtosCodes.Transport, "The companion closed the event stream.");
                            break;
                        }

                        await DeliverAsync(text, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (EtosException error)
                {
                    LastError = error.Error;
                }
                catch (Exception error) when (error is WebSocketException || error is IOException || error is ObjectDisposedException || error is OperationCanceledException || error is InvalidOperationException)
                {
                    LastError = new EtosError(0, EtosCodes.Transport, "The event stream broke: " + error.Message);
                }
                finally
                {
                    lock (_gate)
                    {
                        _socket = null;
                    }

                    socket?.Dispose();
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                broken ??= Stopwatch.StartNew();
                SetState(EventStreamState.Reconnecting, LastError);
                TimeSpan delay = _backoff.Delay(attempt);
                attempt++;
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return;
        }

        private async Task DeliverAsync(string text, CancellationToken token)
        {
            EventFrame? frame = EventFrame.Parse(text);
            if (frame == null || frame.Cursor <= Cursor) return;
            frame.ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                if (HandleAsync != null) await HandleAsync(frame, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Received?.Invoke(frame);
                _cursors.Save(frame.Cursor);
                Interlocked.Exchange(ref _cursor, frame.Cursor);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // Reconnect from the last acknowledged cursor. Never skip ahead after a failed handler/save.
                throw new EtosException(new EtosError(0, "event_handler_failed",
                    "Event cursor " + frame.Cursor.ToString(CultureInfo.InvariantCulture) + " was not acknowledged: " + error.Message));
            }
            return;
        }

        private void SetState(EventStreamState state, EtosError? error)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            Action<EventStreamState, EtosError?>? handler = StateChanged;
            handler?.Invoke(state, error);
        }
    }
}
