// GameCore.Gameplay.Audio - AnimationEventRelay: forwards animation events (footsteps, feedback ids) to the world's IFeedbackSink (P1.5).
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.Audio
{
    /// <summary>Forwards animation events (footsteps and other feedback ids) to an IFeedbackSink.</summary>
    public sealed class AnimationEventRelay : MonoBehaviour
    {
        [SerializeField] private string footstepId = "footstep";
        private bool resolved;

        /// <summary>The sink (the world's IFeedbackSink); resolved from the scene's AudioEngineHost when unset.</summary>
        public IFeedbackSink? Sink { get; set; }

        public int Relayed { get; private set; }

        public string FootstepId
        {
            get => footstepId;
            set => footstepId = value ?? string.Empty;
        }

        /// <summary>Animation event "Footstep" (an optional string parameter overrides the id, e.g. footstep.wood).</summary>
        public void Footstep(string id)
        {
            Relay(string.IsNullOrEmpty(id) ? footstepId : id);
        }

        /// <summary>Animation event "Feedback" with a feedback id.</summary>
        public void Feedback(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                Relay(id);
            }
        }

        private void Relay(string id)
        {
            IFeedbackSink? sink = Sink;
            if (sink == null && !resolved)
            {
                resolved = true;
                AudioEngineHost? host = FindAnyObjectByType<AudioEngineHost>();
                if (host != null && host.Runtime != null)
                {
                    sink = host.Runtime.Sfx;
                    Sink = sink;
                }
            }

            if (sink == null)
            {
                return;
            }

            Vector3 at = transform.position;
            Relayed++;
            sink.Play(id, new System.Numerics.Vector3(at.x, at.y, at.z));
        }
    }
}
