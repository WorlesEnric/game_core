#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>Implemented by the Editor session owning the configured, project-scoped gateway.
    /// The session registers by exposing its current instance; null unregisters on stop/reload.</summary>
    public interface IMediaGenerationGatewayProvider
    {
        IMediaGenerationGateway? MediaGateway { get; }
    }

    /// <summary>Optional sound-effect capability; voice-only providers remain compatible.</summary>
    public interface ISoundEffectGenerationGateway : IMediaGenerationGateway
    {
        MediaGenerationResult RequestSoundEffect(string clipId, string description, int durationMs);
    }

    /// <summary>Resolves existing session registrations, never constructs a provider or retains global state.</summary>
    public static class MediaGenerationLookup
    {
        public static IMediaGenerationGateway Resolve(IEnumerable<object> sessions)
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
            IMediaGenerationGateway? found = null;
            foreach (object session in sessions)
            {
                IMediaGenerationGateway? gateway = (session as IMediaGenerationGatewayProvider)?.MediaGateway;
                if (gateway == null) continue;
                if (found != null && !ReferenceEquals(found, gateway)) return new NotConfiguredMediaGateway();
                found = gateway;
            }

            return found ?? new NotConfiguredMediaGateway();
        }
    }
}
