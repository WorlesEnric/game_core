// GameCore.Unity.Runtime - the world's own declaration of what is queued and staged at a committed boundary.
//
// Normative sources: 00 P-053 ("already queued external commands are either included with ledger/cutoff or explicitly
// rejected before capture according to the checkpoint option, never ambiguously omitted"), P-037/P-042 (a command is
// admitted through the message plane and stays pending until the step that executes it commits) and P-051 (a
// staged, unpublished control-lane operation is not world state).
//
// GC-016's `WorldObservation.LeaseCommittedBoundary` reads these three facts at the instant it leases a boundary, and
// GC-018's `CheckpointCapture` refuses a document whose copied queue contradicts them. Nobody but the owning modules
// can answer them, so this type is the production `ICommittedBoundaryFactsSource`: it reads the world's own request
// plane and the control lane's own pending ledger and invents nothing.
//
// Why it reads live storage rather than a cached value: the lease call reads the three properties *at lease time*,
// and a lease is only granted at an end-of-step or idle boundary where no step, apply or drain is in flight, so a
// live read is the value at that boundary. A world that attaches no source reports `Unspecified` through
// `WorldObservation`'s own default, which is never read as an empty queue.
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>
    /// One world's queued-command and staged-operation facts, read from the world's own plane and lane (P-053).
    /// </summary>
    public sealed class WorldBoundaryFacts : ICommittedBoundaryFactsSource
    {
        private readonly WorldId world;
        private readonly WorldMessagePlane? plane;
        private readonly CompositionHost? lane;
        private readonly BoundaryQueueDisposition disposition;

        /// <param name="world">The world these facts belong to; recorded so a foreign world is diagnosable.</param>
        /// <param name="plane">The world's bounded message plane, or null when it declares none.</param>
        /// <param name="lane">The control lane joined to the world, or null when it declares none.</param>
        /// <param name="disposition">
        /// How this world's capture treats queued commands. A caller that has not chosen a policy passes
        /// <see cref="BoundaryQueueDisposition.Unspecified"/>, which `WorldObservation` reports as "not declared"
        /// instead of an empty queue.
        /// </param>
        public WorldBoundaryFacts(
            WorldId world,
            WorldMessagePlane? plane,
            CompositionHost? lane,
            BoundaryQueueDisposition disposition)
        {
            this.world = world;
            this.plane = plane;
            this.lane = lane;
            this.disposition = disposition;
        }

        /// <summary>The world these facts describe (P-004).</summary>
        public WorldId World => world;

        /// <summary>
        /// Externally admitted commands whose step has not committed yet. Only an external request is a re-admittable
        /// input: an internal request is step-local and does not survive the boundary (P-042), which is the same
        /// filter the committed-boundary reader copies by.
        /// </summary>
        public int QueuedCommandCount
        {
            get
            {
                if (plane == null)
                {
                    return 0;
                }

                IReadOnlyList<RequestRow> pending = plane.Requests.PendingRows();
                int count = 0;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i].Origin == RequestOrigin.External)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Control-lane operations that are admitted and carry a staged proposal not yet published. A pending
        /// operation without a proposal stages nothing, so it is not counted (P-029, P-051).
        /// </summary>
        public int StagedOperationCount
        {
            get
            {
                if (lane == null)
                {
                    return 0;
                }

                IReadOnlyList<OperationId> pending = lane.OperationLedger.PendingInAdmissionOrder();
                int count = 0;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (lane.StagedPlan(pending[i]) != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>The disposition the caller declared for this world's queued commands (P-053).</summary>
        public BoundaryQueueDisposition QueueDisposition => disposition;

        public override string ToString() =>
            "boundaryFacts(" + world.Session.ToString()
            + ",queued=" + QueuedCommandCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",staged=" + StagedOperationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "," + disposition.ToString() + ")";
    }
}
