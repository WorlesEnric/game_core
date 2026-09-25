// GameCore.Composition tests — the lane seed that joins a control lane to a world's published assembly (GC-008).
//
// P-006 defines ONE publication series: `CompositionRevision` and `AssemblyEpoch` increment at the same publication.
// 05 s2 fixes that a fresh session's initial assembly publishes revision/epoch 1 while the step stays 0. A lane
// joined to a world which has published that assembly therefore starts at the world's published counters and every
// admission moves both by one — so the epoch a composition operation reports IS the epoch the world publishes, with
// no offset for a later subsystem to reconcile.
//
// The cases below pin that down on both sides: a seeded lane honours the world's counters and keeps them together,
// and a lane with no seed keeps the standalone pre-publication 0/0 behaviour the other GC-004 tests rely on.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class LaneSeedTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x73656564UL, 8UL));
        private static readonly IdFactory Ids = new IdFactory(0x73656564UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 8UL));

        private static CompositionHost NewHost(CompositionLaneSeed seed)
        {
            var source = new TestManifestSource();
            return new CompositionHost(
                World,
                Root,
                new CompositionHostSettings(
                    new ControlLaneCapacitySettings(8, 8),
                    new OperationExpirySettings(3, 0UL)),
                source,
                null,
                PropagationMode.Automatic,
                seed);
        }

        private static CompositionHost NewStandaloneHost() => NewHost(default(CompositionLaneSeed));

        private static CompositionEditPayload MountPayload()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            PluginManifest manifest = Manifests.Plain(type, Ids);
            return Payloads.Mount(manifest, instance, Root, ConfigDocument.Empty);
        }

        /// <summary>One admitted, published mount; returns the committed epoch it produced (P-006).</summary>
        private static ulong AdmitAndPublish(CompositionHost host, OperationIssuer issuer)
        {
            EditAdmission admission = host.SubmitEdit(MountPayload(), issuer.Next(), host.Committed.Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());

            IReadOnlyList<PublishedOperation> published = host.Drain();
            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].Outcome, Is.EqualTo(Outcome.Published), published[0].Code.ToString());
            return host.Committed.Epoch.Value;
        }

        [Test]
        public void ASeedJoinsTheLaneToTheWorldsPublishedAssembly()
        {
            CompositionHost host = NewHost(CompositionLaneSeed.InitialAssembly);

            Assert.That(host.Seed.IsJoined, Is.True);
            Assert.That(host.Seed.IsConsistent, Is.True, "P-006 increments revision and epoch together");
            Assert.That(host.Committed.Revision, Is.EqualTo(CompositionRevision.First));
            Assert.That(host.Committed.Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(host.Committed.Revision.Value, Is.EqualTo(host.Committed.Epoch.Value),
                "a joined lane reports one publication series, not two counters");
            Assert.That(host.Committed.Step, Is.EqualTo(LogicalStepId.Zero), "the initial assembly keeps step 0 (05 s2)");
            Assert.That(host.PublicationCount, Is.EqualTo(0), "the seed is the world's assembly, not a publication here");
            Assert.That(host.OperationLedger.PublishedRevision, Is.EqualTo(CompositionRevision.Zero),
                "the ledger starts empty; the seed is the world's publication, not this lane's");
        }

        [Test]
        public void AStandaloneLaneKeepsThePrePublicationCounters()
        {
            CompositionHost host = NewStandaloneHost();

            Assert.That(host.Seed.IsUnpublished, Is.True);
            Assert.That(host.Seed.IsJoined, Is.False);
            Assert.That(host.Committed.Revision, Is.EqualTo(CompositionRevision.Zero));
            Assert.That(host.Committed.Epoch, Is.EqualTo(AssemblyEpoch.Zero));
            Assert.That(host.Committed.Step, Is.EqualTo(LogicalStepId.Zero));
        }

        [Test]
        public void EveryPublicationMovesRevisionAndEpochTogether()
        {
            CompositionHost host = NewHost(CompositionLaneSeed.InitialAssembly);
            var issuer = new OperationIssuer(World, new Id128(0x73656564UL, 0x0100UL));

            for (ulong sequence = 1UL; sequence <= 4UL; sequence++)
            {
                ulong epoch = AdmitAndPublish(host, issuer);

                Assert.That(epoch, Is.EqualTo(sequence + 1UL),
                    "the seeded lane's first publication is the world's second assembly (P-006)");
                Assert.That(host.Committed.Revision.Value, Is.EqualTo(epoch),
                    "revision and epoch name the same publication (P-006)");
            }

            Assert.That(host.PublicationCount, Is.EqualTo(4));
            Assert.That(host.Committed.Epoch.Value, Is.EqualTo(5UL));
            Assert.That(host.Committed.Revision.Value, Is.EqualTo(5UL));
        }

        [Test]
        public void AStandaloneLaneStillPublishesFromZero()
        {
            CompositionHost host = NewStandaloneHost();
            var issuer = new OperationIssuer(World, new Id128(0x73656564UL, 0x0200UL));

            Assert.That(AdmitAndPublish(host, issuer), Is.EqualTo(1UL),
                "with no seed the first publication is revision/epoch 1 (05 s2)");
            Assert.That(AdmitAndPublish(host, issuer), Is.EqualTo(2UL));
            Assert.That(host.Committed.Revision.Value, Is.EqualTo(host.Committed.Epoch.Value));
        }

        [Test]
        public void AnInconsistentSeedIsRefused()
        {
            // Revision and epoch must name one publication; two different numbers would reintroduce the split.
            var inconsistent = new CompositionLaneSeed(new CompositionRevision(3UL), new AssemblyEpoch(1UL));

            Assert.That(inconsistent.IsConsistent, Is.False);
            Assert.That(inconsistent.ToString(), Does.Contain("INCONSISTENT"));
            Assert.That(CompositionLaneSeed.InitialAssembly.IsConsistent, Is.True);
            Assert.That(CompositionLaneSeed.Unpublished.IsConsistent, Is.True);

            var source = new TestManifestSource();
            Assert.That(
                () => new CompositionHost(
                    World,
                    Root,
                    new CompositionHostSettings(
                        new ControlLaneCapacitySettings(8, 8),
                        new OperationExpirySettings(3, 0UL)),
                    source,
                    null,
                    PropagationMode.Automatic,
                    inconsistent),
                Throws.ArgumentException.With.Message.Contains("inconsistent"));
        }

        [Test]
        public void TheSeedIsVisibleOnTheHostAndItsCommittedSnapshot()
        {
            CompositionHost host = NewHost(CompositionLaneSeed.FromPublishedAssembly(
                new CompositionRevision(7UL),
                new AssemblyEpoch(7UL)));

            Assert.That(host.Seed.Revision.Value, Is.EqualTo(7UL));
            Assert.That(host.Seed.Epoch.Value, Is.EqualTo(7UL));

            CompositionStateSnapshot snapshot = host.Snapshot();
            Assert.That(snapshot.Revision.Value, Is.EqualTo(7UL), "the committed view starts where the world is");
            Assert.That(snapshot.Epoch.Value, Is.EqualTo(7UL));
            Assert.That(snapshot.Step, Is.EqualTo(LogicalStepId.Zero));

            // A publication from a seeded lane names the next value of that one series, not an offset from zero.
            var issuer = new OperationIssuer(World, new Id128(0x73656564UL, 0x0300UL));
            Assert.That(AdmitAndPublish(host, issuer), Is.EqualTo(8UL));
            Assert.That(host.Committed.Revision.Value, Is.EqualTo(8UL));
        }
    }
}
