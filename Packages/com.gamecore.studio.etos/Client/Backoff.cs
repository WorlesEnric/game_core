// GameCore.Studio.Etos.Client - reconnect schedule: exponential backoff with "equal jitter". Attempt n waits
// cap = min(Max, Base * 2^n) and a delay drawn uniformly from [cap/2, cap]. With the defaults the first reconnect
// happens within 0.25 s and the third within 1 s, so a dropped event stream is back within the 5 s budget of
// B-AGENT-UX while a node that stays down is polled at most every 30 s.
#nullable enable
using System;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Exponential backoff with jitter; deterministic for a seeded <see cref="Random"/>.</summary>
    public sealed class BackoffPolicy
    {
        private readonly Random _random;
        private readonly object _gate = new object();

        public BackoffPolicy(TimeSpan? baseDelay = null, TimeSpan? maxDelay = null, Random? random = null)
        {
            Base = baseDelay ?? TimeSpan.FromMilliseconds(250);
            Max = maxDelay ?? TimeSpan.FromSeconds(30);
            if (Base <= TimeSpan.Zero || Max < Base)
            {
                throw new ArgumentException("Backoff needs 0 < base <= max.");
            }

            _random = random ?? new Random();
        }

        public TimeSpan Base { get; }

        public TimeSpan Max { get; }

        /// <summary>The upper bound of attempt <paramref name="attempt"/> (0-based): min(Max, Base * 2^attempt).</summary>
        public TimeSpan Cap(int attempt)
        {
            if (attempt < 0)
            {
                attempt = 0;
            }

            double ms = Base.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 30));
            return TimeSpan.FromMilliseconds(Math.Min(ms, Max.TotalMilliseconds));
        }

        /// <summary>The delay before attempt <paramref name="attempt"/>: uniform in [Cap/2, Cap].</summary>
        public TimeSpan Delay(int attempt)
        {
            double cap = Cap(attempt).TotalMilliseconds;
            double sample;
            lock (_gate)
            {
                sample = _random.NextDouble();
            }

            return TimeSpan.FromMilliseconds(cap / 2 + sample * cap / 2);
        }
    }
}
