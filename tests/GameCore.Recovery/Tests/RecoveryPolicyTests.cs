// GC-027's recovery-policy suite (P-049, P-050, TEST-016).
//
// P-049 permits bounded retries of a *transient resource* failure and requires changed input for a correctness
// failure; P-022 says a bound is raised by configuration, never by a retry loop; P-050 says a retry is a new
// operation id and a reserved session is never handed out twice. The production types under test are
// `RecoveryFailureClassification` (the pure code -> classification function), `RecoveryRetryPolicy` (the host's
// bound, clamped, applied to that classification) and `RecoveryAttemptLog` (the bounded transcript in which
// "a retry reserved a fresh session and a new operation" is observable rather than asserted in prose).
//
// The host setting the bound comes from is `OperationExpirySettings.BoundedTransientAttempts`, which the W5/W6 gates
// recorded as "stored, never enforced" before this task: `FromHostSettings(null)` returning exactly one attempt is
// the clause that closes it.
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution.Recovery;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>The retry bound, the failure classification and the bounded attempt transcript of one recovery.</summary>
    [TestFixture]
    public sealed class RecoveryPolicyTests
    {
        private static RecoveryRetryPolicy ThreeAttempts() =>
            RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(3, 0UL));

        [Test]
        public void NoHostSettingMeansExactlyOneAttempt()
        {
            RecoveryRetryPolicy policy = RecoveryRetryPolicy.FromHostSettings(null);

            Assert.That(policy.MaxAttempts, Is.EqualTo(1), "P-049 permits bounded retries; it never requires them.");
            Assert.That(policy.MaxRetries, Is.EqualTo(0));
            Assert.That(policy.ConfiguredAttempts, Is.EqualTo(0), "no host bound was supplied.");
            Assert.That(policy.WasClamped, Is.False);
            Assert.That(policy.AllowsRetry(1, DiagnosticCode.ResourceUnavailable), Is.False,
                "with one attempt there is no second one, even for a retryable failure.");
            Assert.That(policy.AllowsRetry(0, DiagnosticCode.ResourceUnavailable), Is.False);
            Assert.That(RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(0, 0UL)).MaxAttempts, Is.EqualTo(1),
                "a non-positive bound is not a bound; the single-attempt default applies.");
            Assert.That(RecoveryRetryPolicy.SingleAttempt.MaxAttempts, Is.EqualTo(1));
        }

        [Test]
        public void AHostBoundOfThreeAllowsTwoRetriesForARetryableFailure()
        {
            RecoveryRetryPolicy policy = ThreeAttempts();

            Assert.That(policy.MaxAttempts, Is.EqualTo(3));
            Assert.That(policy.MaxRetries, Is.EqualTo(2));
            Assert.That(policy.ConfiguredAttempts, Is.EqualTo(3));
            Assert.That(policy.WasClamped, Is.False);

            Assert.That(policy.AllowsRetry(1, DiagnosticCode.ResourceUnavailable), Is.True,
                "a transient resource failure is retryable while the host bound is not exhausted.");
            Assert.That(policy.AllowsRetry(2, DiagnosticCode.ResourceUnavailable), Is.True);
            Assert.That(policy.AllowsRetry(3, DiagnosticCode.ResourceUnavailable), Is.False,
                "the bound is the total number of attempts, not the number of retries.");
            Assert.That(policy.AllowsRetry(4, DiagnosticCode.ResourceUnavailable), Is.False);
            Assert.That(policy.ToString(), Does.Contain("retry(max=3"));
        }

        [Test]
        public void AnOversizedHostBoundIsClampedAndReported()
        {
            RecoveryRetryPolicy policy = RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(1000, 0UL));

            Assert.That(policy.MaxAttempts, Is.EqualTo(RecoveryRetryPolicy.MaxConfiguredAttempts),
                "a host setting is configuration, not an unbounded loop (P-022).");
            Assert.That(policy.MaxAttempts, Is.EqualTo(16));
            Assert.That(policy.ConfiguredAttempts, Is.EqualTo(1000), "the configured value is still reported.");
            Assert.That(policy.WasClamped, Is.True);
            Assert.That(policy.MaxRetries, Is.EqualTo(RecoveryRetryPolicy.MaxConfiguredAttempts - 1));
            Assert.That(policy.AllowsRetry(RecoveryRetryPolicy.MaxConfiguredAttempts - 1, DiagnosticCode.ResourceUnavailable), Is.True);
            Assert.That(policy.AllowsRetry(RecoveryRetryPolicy.MaxConfiguredAttempts, DiagnosticCode.ResourceUnavailable), Is.False);
            Assert.That(policy.ToString(), Does.Contain("clampedFrom=1000"));
        }

        [Test]
        public void ACorrectnessFailureNeverAllowsARetryWithAttemptsLeft()
        {
            RecoveryRetryPolicy policy = ThreeAttempts();
            DiagnosticCode[] correctness =
            {
                DiagnosticCode.UnsupportedVersion,
                DiagnosticCode.MigrationRequired,
                DiagnosticCode.IdempotencyConflict,
            };

            foreach (DiagnosticCode code in correctness)
            {
                Assert.That(RecoveryFailureClassification.RetryOf(code), Is.EqualTo(RetryClassification.RequiresChangedInput),
                    code + " needs changed input or a changed catalog, not another attempt (P-049).");
                Assert.That(RecoveryFailureClassification.Of(code),
                    Is.EqualTo(FailureClassification.CorrectnessRequiresChangedInput));
                Assert.That(policy.AllowsRetry(1, code), Is.False, "attempts were left, but the failure is not retryable.");
                Assert.That(policy.AllowsRetry(2, code), Is.False);
            }
        }

        [Test]
        public void ACorrectnessFailureOfACorrectedCatalogIsStillNotRetriedInPlace()
        {
            // The same code with the maximum host bound still refuses a retry: the classification, not the bound, is
            // what stops it (P-049's "correctness failures require changed input/catalog").
            RecoveryRetryPolicy policy = RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(1000, 0UL));
            Assert.That(policy.AllowsRetry(1, DiagnosticCode.UnsupportedVersion), Is.False);
            Assert.That(RecoveryFailureClassification.Describe(DiagnosticCode.UnsupportedVersion),
                Is.EqualTo("UnsupportedVersion:CorrectnessRequiresChangedInput/RequiresChangedInput"));
        }

        [Test]
        public void ATerminalStateFailureIsNotRetryable()
        {
            RecoveryRetryPolicy policy = ThreeAttempts();
            DiagnosticCode[] terminal =
            {
                DiagnosticCode.ApplyFault,
                DiagnosticCode.TooLate,
                DiagnosticCode.StaleHandle,
                DiagnosticCode.Cancelled,
            };

            foreach (DiagnosticCode code in terminal)
            {
                Assert.That(RecoveryFailureClassification.RetryOf(code), Is.EqualTo(RetryClassification.NotRetryable),
                    code + " is a terminal state of the operation attempt, not a transient resource failure.");
                Assert.That(RecoveryFailureClassification.Of(code), Is.EqualTo(FailureClassification.Fatal));
                Assert.That(policy.AllowsRetry(1, code), Is.False);
            }

            Assert.That(RecoveryFailureClassification.Of(DiagnosticCode.None), Is.EqualTo(FailureClassification.Fatal),
                "no failure is the one code nothing is retried for.");
        }

        [Test]
        public void ABudgetFailureIsNeverRetried()
        {
            RecoveryRetryPolicy policy = ThreeAttempts();

            Assert.That(RecoveryFailureClassification.RetryOf(DiagnosticCode.BudgetExceeded),
                Is.EqualTo(RetryClassification.RequiresChangedInput),
                "raising a budget is an explicit configuration change, not a retry loop (P-022).");
            Assert.That(RecoveryFailureClassification.Of(DiagnosticCode.BudgetExceeded),
                Is.EqualTo(FailureClassification.CorrectnessRequiresChangedInput));
            Assert.That(policy.AllowsRetry(1, DiagnosticCode.BudgetExceeded), Is.False);
            Assert.That(policy.AllowsRetry(2, DiagnosticCode.BudgetExceeded), Is.False);
        }

        [Test]
        public void OfAndRetryOfAgreeForEveryDiagnosticCode()
        {
            RecoveryRetryPolicy policy = ThreeAttempts();
            int retryable = 0;
            int requiresChangedInput = 0;
            int notRetryable = 0;

            foreach (DiagnosticCode code in (DiagnosticCode[])Enum.GetValues(typeof(DiagnosticCode)))
            {
                RetryClassification retry = RecoveryFailureClassification.RetryOf(code);
                FailureClassification failure = RecoveryFailureClassification.Of(code);

                switch (retry)
                {
                    case RetryClassification.RetrySameInput:
                        retryable++;
                        Assert.That(failure, Is.EqualTo(FailureClassification.Retriable), code.ToString());
                        Assert.That(policy.AllowsRetry(1, code), Is.True,
                            code + " is the retryable class, so the bound is what stops the next attempt.");
                        break;

                    case RetryClassification.RequiresChangedInput:
                        requiresChangedInput++;
                        Assert.That(failure, Is.EqualTo(FailureClassification.CorrectnessRequiresChangedInput), code.ToString());
                        Assert.That(policy.AllowsRetry(1, code), Is.False, code.ToString());
                        break;

                    default:
                        notRetryable++;
                        Assert.That(retry, Is.EqualTo(RetryClassification.NotRetryable), code.ToString());
                        Assert.That(failure, Is.EqualTo(FailureClassification.Fatal), code.ToString());
                        Assert.That(policy.AllowsRetry(1, code), Is.False, code.ToString());
                        break;
                }

                Assert.That(RecoveryFailureClassification.Describe(code),
                    Is.EqualTo(DiagnosticCodeText.Of(code) + ":" + failure.ToString() + "/" + retry.ToString()),
                    code + ": the description composes the two classifications.");
            }

            Assert.That(retryable, Is.EqualTo(1), "only a transient resource failure is retried with the same input.");
            Assert.That(requiresChangedInput + notRetryable, Is.EqualTo(Enum.GetValues(typeof(DiagnosticCode)).Length - 1));
            Assert.That(RecoveryFailureClassification.RetryOf(DiagnosticCode.ResourceUnavailable),
                Is.EqualTo(RetryClassification.RetrySameInput));
            Assert.That(RecoveryFailureClassification.Describe(DiagnosticCode.ResourceUnavailable),
                Is.EqualTo("ResourceUnavailable:Retriable/RetrySameInput"));
        }

        [Test]
        public void TheAttemptLogRecordsAttemptsInOrderAndCountsOverflow()
        {
            var log = new RecoveryAttemptLog(2);
            Assert.That(log.Capacity, Is.EqualTo(2));
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Describe(), Is.EqualTo("<empty>"));
            Assert.That(log.Last, Is.Null);

            Assert.That(log.TryAdd(Attempt(0, RecoveryAttemptKind.Initial, false, 1UL, 1UL,
                DiagnosticCode.ResourceUnavailable, RecoveryFaultPoints.CheckpointPublication)), Is.True);
            Assert.That(log.TryAdd(Attempt(1, RecoveryAttemptKind.Retry, true, 2UL, 2UL,
                DiagnosticCode.None, string.Empty)), Is.True);
            Assert.That(log.TryAdd(Attempt(2, RecoveryAttemptKind.Retry, true, 3UL, 3UL,
                DiagnosticCode.None, string.Empty)), Is.False, "the log is bounded; it never grows past its capacity.");

            Assert.That(log.Count, Is.EqualTo(2));
            Assert.That(log.OverflowCount, Is.EqualTo(1), "a refused record is counted, never silently dropped (P-043).");
            Assert.That(log.RetryCount, Is.EqualTo(1));
            Assert.That(log.PublishedCount, Is.EqualTo(1));
            Assert.That(log.AttemptsAreDistinct(), Is.True);
            Assert.That(log.Records[0].Failed, Is.True);
            Assert.That(log.Records[1].Failed, Is.False);
            Assert.That(log.Records[0].FaultPointId, Is.EqualTo(RecoveryFaultPoints.CheckpointPublication));
            Assert.That(log.Records[0].ToLine(), Does.Contain("at=" + RecoveryFaultPoints.CheckpointPublication));
            Assert.That(log.Last, Is.Not.Null);
            Assert.That(log.Last!.Ordinal, Is.EqualTo(1));
            Assert.That(log.Describe(), Is.EqualTo(log.Records[0].ToLine() + "\n" + log.Records[1].ToLine()),
                "the transcript is LF-joined canonical lines, in attempt order.");
            Assert.That(log.ToString(), Does.Contain("attempts(2/2,retries=1)"));

            Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryAttemptLog(0));
            Assert.Throws<ArgumentNullException>(() => log.TryAdd(null!));
        }

        [Test]
        public void TheAttemptLogRequiresDistinctSessionsAndOperations()
        {
            var log = new RecoveryAttemptLog(4);
            RecoveryAttemptRecord initial = Attempt(0, RecoveryAttemptKind.Initial, false, 1UL, 1UL,
                DiagnosticCode.ResourceUnavailable, RecoveryFaultPoints.Restart);
            RecoveryAttemptRecord retry = Attempt(1, RecoveryAttemptKind.Retry, false, 2UL, 2UL,
                DiagnosticCode.ResourceUnavailable, RecoveryFaultPoints.Restart);

            Assert.That(log.TryAdd(initial), Is.True);
            Assert.That(log.TryAdd(retry), Is.True);
            Assert.That(log.AttemptsAreDistinct(), Is.True,
                "a retry reserves a fresh session and a new operation id (P-050).");

            var reusedSession = new RecoveryAttemptRecord(
                2,
                RecoveryAttemptKind.Retry,
                RecoverySourceKind.Checkpoint,
                initial.Destination,
                new OperationId(initial.Destination, new Id128(0x2222UL, 0x3333UL), 9UL),
                Outcome.Published,
                DiagnosticCode.None,
                string.Empty,
                string.Empty);
            Assert.That(log.TryAdd(reusedSession), Is.True);
            Assert.That(log.AttemptsAreDistinct(), Is.False,
                "the faulted session is never resumed, including after a failed attempt (P-050).");
        }

        private static RecoveryAttemptRecord Attempt(
            int ordinal,
            RecoveryAttemptKind kind,
            bool published,
            ulong sessionOrdinal,
            ulong operationOrdinal,
            DiagnosticCode code,
            string faultPointId)
        {
            var session = new WorldId(new Id128(0x1000UL, sessionOrdinal));
            var operation = new OperationId(session, new Id128(0x2000UL, sessionOrdinal), operationOrdinal);
            return new RecoveryAttemptRecord(
                ordinal,
                kind,
                RecoverySourceKind.Checkpoint,
                session,
                operation,
                published ? Outcome.Published : Outcome.Faulted,
                code,
                published ? "published into a new session" : "refused with a code",
                faultPointId);
        }
    }
}
