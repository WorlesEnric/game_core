#nullable enable
using GameCore.Gameplay.Contracts.Narrative;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Local
{
    public sealed class R3DMediaGatewayTests
    {
        [Test]
        public void D7_PairedProviderLookupTracksCurrentGatewayWithoutCaching()
        {
            var first = new Gateway();
            var second = new Gateway();
            var session = new Provider { MediaGateway = first };
            Assert.That(MediaGenerationLookup.Resolve(new object[] { session }), Is.SameAs(first));
            session.MediaGateway = second;
            var resolved = MediaGenerationLookup.Resolve(new object[] { session });
            var result = resolved.RequestVoiceLine(new VoiceGenerationRequest("graph", 0, "Odd", "The shrine is lit.", "fake"));
            Assert.That(result.Status, Is.EqualTo(MediaGenerationStatus.Requested));
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(first.Calls, Is.Zero);
            session.MediaGateway = null;
            Assert.That(MediaGenerationLookup.Resolve(new object[] { session }), Is.TypeOf<NotConfiguredMediaGateway>());
        }

        [Test]
        public void D7_AmbiguousProvidersRefuseWithoutCallingEitherGateway()
        {
            var first = new Gateway();
            var second = new Gateway();
            var resolved = MediaGenerationLookup.Resolve(new object[]
            {
                new Provider { MediaGateway = first }, new Provider { MediaGateway = second },
            });
            Assert.That(resolved, Is.TypeOf<NotConfiguredMediaGateway>());
            Assert.That(first.Calls + second.Calls, Is.Zero);
        }

        private sealed class Provider : IMediaGenerationGatewayProvider
        {
            public IMediaGenerationGateway? MediaGateway { get; set; }
        }

        private sealed class Gateway : IMediaGenerationGateway
        {
            public int Calls { get; private set; }
            public MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request)
            {
                Calls++;
                return new MediaGenerationResult(MediaGenerationStatus.Requested, "fake", "retained workflow regression");
            }
        }
    }
}
