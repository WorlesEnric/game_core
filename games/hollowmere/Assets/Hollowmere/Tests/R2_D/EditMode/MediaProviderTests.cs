#nullable enable
using System;
using System.Linq;
using System.Reflection;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Etos;
using GameCore.Studio.Hollowmere.P2_2;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R2_D
{
    public sealed class MediaProviderTests
    {
        private EtosStudioSession _session = null!;
        private object? _previousGateway;

        [SetUp]
        public void SetUp()
        {
            _session = EtosStudioSession.instance;
            _previousGateway = GatewayField.GetValue(_session);
            Bind(null);
        }

        [TearDown]
        public void TearDown()
        {
            GatewayField.SetValue(_session, _previousGateway);
            // Reconcile any cached adapter with the restored session binding.
            _ = (_session as object as IMediaGenerationGatewayProvider)?.MediaGateway;
        }

        [Test]
        public void R2_41_ConfiguredSessionIsTheOnlyActiveProviderAndCachesAdapter()
        {
            using var h = GatewayHarness.WithFake();
            Bind(h.Gateway);
            IMediaGenerationGatewayProvider provider = Provider();
            var active = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                .OfType<IMediaGenerationGatewayProvider>().Where(p => p.MediaGateway != null).ToArray();
            Assert.That(active, Has.Length.EqualTo(1));
            Assert.That(active[0], Is.SameAs(provider));
            Assert.That(Resolve(), Is.TypeOf<EtosMediaGenerator>());
            Assert.That(Resolve(), Is.SameAs(provider.MediaGateway));
            Assert.That(provider.MediaGateway, Is.Not.InstanceOf<ISoundEffectGenerationGateway>());
        }

        [Test]
        public void R2_41_UnpairedSessionExposesNoActiveProvider()
        {
            Assert.That(Provider().MediaGateway, Is.Null);
            Assert.That(Resources.FindObjectsOfTypeAll<ScriptableObject>()
                .OfType<IMediaGenerationGatewayProvider>().Count(p => p.MediaGateway != null), Is.Zero);
            Assert.That(Resolve(), Is.TypeOf<NotConfiguredMediaGateway>());
        }

        [TestCase("gateway")]
        [TestCase("project")]
        [TestCase("app")]
        public void R2_41_RebindingRebuildsCachedMediaGateway(string binding)
        {
            using var first = GatewayHarness.WithFake();
            using var second = GatewayHarness.WithFake();
            Bind(first.Gateway);
            IMediaGenerationGateway? previous = Provider().MediaGateway;
            Assert.That(previous, Is.TypeOf<EtosMediaGenerator>());
            if (binding == "gateway") Bind(second.Gateway);
            else if (binding == "project") first.Client.Options.ProjectId = new string('b', 64);
            else first.Client.Options.AppName = "another-fake-app";
            IMediaGenerationGateway? rebound = Provider().MediaGateway;
            Assert.That(rebound, Is.TypeOf<EtosMediaGenerator>().And.Not.SameAs(previous));
            Assert.That(Resolve(), Is.SameAs(rebound));
            var field = typeof(EtosMediaGenerator).GetField("_gateway", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field.GetValue(rebound), Is.SameAs(binding == "gateway" ? second.Gateway : first.Gateway));
        }

        [Test]
        public void R2_41_ReloadTeardownUnregistersAndRestartCreatesFreshAdapter()
        {
            using var first = GatewayHarness.WithFake();
            using var second = GatewayHarness.WithFake();
            Bind(first.Gateway);
            IMediaGenerationGateway? previous = Provider().MediaGateway;
            typeof(EtosStudioSession).GetMethod("OnBeforeReload", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_session, null);
            Assert.That(Provider().MediaGateway, Is.Null);
            Assert.That(Resolve(), Is.TypeOf<NotConfiguredMediaGateway>());
            Bind(second.Gateway);
            Assert.That(Resolve(), Is.TypeOf<EtosMediaGenerator>().And.Not.SameAs(previous));
            EtosStudioSession.Stop();
            Assert.That(Provider().MediaGateway, Is.Null);
            Assert.That(Resolve(), Is.TypeOf<NotConfiguredMediaGateway>());
        }

        private static FieldInfo GatewayField => typeof(EtosStudioSession)
            .GetField("_gateway", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private void Bind(EtosAgentGateway? gateway) => GatewayField.SetValue(_session, gateway);

        private IMediaGenerationGatewayProvider Provider()
        {
            Assert.That(_session, Is.InstanceOf<IMediaGenerationGatewayProvider>(), "R2-41: session must publish the media gateway");
            return (IMediaGenerationGatewayProvider)(object)_session;
        }

        private static IMediaGenerationGateway Resolve() =>
            MediaGenerationLookup.Resolve(Resources.FindObjectsOfTypeAll<ScriptableObject>());
    }
}
