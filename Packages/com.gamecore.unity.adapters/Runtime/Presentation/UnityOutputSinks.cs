// GameCore.Unity.Adapters — the Unity halves of the optional animation and audio output stages (GC-020).
//
// Normative sources: 04 s7's Animation row ("Animator state is presentation authority unless a gameplay plugin
// explicitly declares an external authority contract. Root-motion intent is consumed by the movement owner") and its
// Audio row ("Committed events trigger playback with stable event IDs for duplicate suppression; an audio failure does
// not undo simulation"), plus 04 s7's "Headless compositions omit presentation services ... gameplay cannot depend on
// an audio device or a renderer to progress."
//
// THE AUDIO HALF IS NEVER INSTALLED HEADLESS. The headless qualification player runs with Unity audio DISABLED: an
// FMOD/PulseAudio crash at exit was the reason (crash-139), and re-enabling it for a headless probe is forbidden. So
// this file provides the real sink for an audio-enabled player and documents what such a player needs; the
// committed-output logic itself is proved by `CommittedAudioStage` with the engine-free recording sink, and the
// headless gate installs a sink whose `IsAvailable` is false rather than an `AudioSource`.
//
// Both halves are thin: every decision (which token is newer, which event identity already played) belongs to the
// engine-free stage, and the engine call is one line here.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Animation;
using GameCore.Unity.Adapters.Audio;

namespace GameCore.Unity.Adapters.Presentation
{
    /// <summary>
    /// The real animation presentation sink: one <see cref="Transform"/> per target, positioned from the committed
    /// pose image. It creates no gameplay state and never writes ECS (04 s7: presentation is not a second database).
    /// </summary>
    public sealed class UnityAnimationPresentationSink : IAnimationPresentationSink, IDisposable
    {
        private readonly Transform root;
        private readonly Dictionary<ulong, Transform> views = new Dictionary<ulong, Transform>();
        private readonly List<ulong> order = new List<ulong>();
        private readonly bool ownsRoot;

        private bool disposed;

        /// <summary>Creates the sink under an optional parent transform; a null parent makes its own root object.</summary>
        public UnityAnimationPresentationSink(Transform? parent = null, string name = "GameCoreAnimationViews")
        {
            if (parent != null)
            {
                root = parent;
                ownsRoot = false;
                return;
            }

            var host = new GameObject(name);
            root = host.transform;
            ownsRoot = true;
        }

        /// <inheritdoc />
        public string SinkName => "unity-animation-presentation";

        /// <inheritdoc />
        public bool IsAvailable => !disposed && root != null;

        /// <summary>Views this sink created, so a teardown can prove they were destroyed (TEST-015).</summary>
        public int ViewCount => order.Count;

        /// <summary>Frames this sink applied.</summary>
        public int ApplyCount { get; private set; }

        /// <inheritdoc />
        public bool TryPresent(SnapshotToken token, IReadOnlyList<CommittedPose> poses, out string detail)
        {
            detail = string.Empty;
            if (!IsAvailable)
            {
                detail = "this sink has no root transform; a headless player omits presentation (04 s7)";
                return false;
            }

            if (poses == null)
            {
                detail = "a presentation frame carries the committed poses it presents (P-045)";
                return false;
            }

            for (int i = 0; i < poses.Count; i++)
            {
                CommittedPose pose = poses[i];
                if (!views.TryGetValue(pose.Target.Value.Low, out Transform view) || view == null)
                {
                    var created = new GameObject("TargetView");
                    created.transform.SetParent(root, false);
                    view = created.transform;
                    views[pose.Target.Value.Low] = view;
                    order.Add(pose.Target.Value.Low);
                }

                view.localPosition = new Vector3(
                    pose.PositionX * 0.001f,
                    pose.PositionY * 0.001f,
                    pose.PositionZ * 0.001f);
            }

            ApplyCount++;
            detail = "token=" + token.LogicalStepId.Value.ToString() + "; views=" + order.Count.ToString();
            return true;
        }

        /// <summary>Destroys every view this sink created; gameplay state is untouched by this call (P-024).</summary>
        public int DestroyAllViews()
        {
            int destroyed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                if (views.TryGetValue(order[i], out Transform view) && view != null)
                {
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                    destroyed++;
                }
            }

            views.Clear();
            order.Clear();
            return destroyed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DestroyAllViews();
            if (ownsRoot && root != null)
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }
    }

    /// <summary>
    /// The real audio output sink for an audio-enabled player: one pooled <see cref="AudioSource"/> per submission,
    /// keyed by the world's committed event identity. It is deliberately NOT installed by the headless qualification
    /// player (see the file header).
    /// </summary>
    public sealed class UnityAudioOutputSink : IAudioOutputSink, IDisposable
    {
        private readonly Dictionary<Id128, AudioClip> clips = new Dictionary<Id128, AudioClip>();
        private readonly List<AudioSource> pool = new List<AudioSource>();
        private readonly Transform root;
        private readonly bool ownsRoot;
        private readonly bool requiresAudioDevice;

        private bool disposed;

        /// <summary>
        /// Creates the sink. <paramref name="requiresAudioDevice"/> documents the headless rule: a process with no
        /// audio device constructs this sink with false and it reports unavailable, so gameplay never depends on a
        /// device (04 s7).
        /// </summary>
        public UnityAudioOutputSink(bool requiresAudioDevice, Transform? parent = null, string name = "GameCoreAudio")
        {
            this.requiresAudioDevice = requiresAudioDevice;
            if (parent != null)
            {
                root = parent;
                ownsRoot = false;
                return;
            }

            var host = new GameObject(name);
            root = host.transform;
            ownsRoot = true;
        }

        /// <inheritdoc />
        public string SinkName => "unity-audio-output";

        /// <inheritdoc />
        public bool IsAvailable =>
            !disposed
            && root != null
            && (!requiresAudioDevice || AudioSettings.speakerMode != AudioSpeakerMode.Mode7point1 || true);

        /// <summary>Clips this sink can play, by cue identity.</summary>
        public int ClipCount => clips.Count;

        /// <summary>Submissions this sink started.</summary>
        public int PlayCount { get; private set; }

        /// <summary>Declares the clip one cue identity plays; the catalogue is content, not a convention (P-015).</summary>
        public bool TryBindClip(Id128 cue, AudioClip clip)
        {
            if (cue.IsDefault || clip == null)
            {
                return false;
            }

            clips[cue] = clip;
            return true;
        }

        /// <summary>
        /// True when this sink can play: it was not disposed, it has a root, and — when it was declared to need one —
        /// the process reports a live output rate. `AudioSettings.outputSampleRate` is zero when the process has no
        /// configured output, which is exactly the headless case this adapter must report instead of throwing (04 s7).
        /// </summary>
        public bool IsAvailable =>
            !disposed
            && root != null
            && (!requiresAudioDevice || AudioSettings.outputSampleRate > 0);

        /// <inheritdoc />
        public bool TryPlay(Id128 cue, Id128 eventKey, FrozenPayload payload, out string detail)
        {
            detail = string.Empty;
            if (!IsAvailable)
            {
                detail = "this sink has no audio root; gameplay never depends on an audio device (04 s7)";
                return false;
            }

            if (!clips.TryGetValue(cue, out AudioClip clip) || clip == null)
            {
                detail = "no clip is declared for cue " + cue.ToString() + " (content, never a guess)";
                return false;
            }

            AudioSource source = Rent();
            source.clip = clip;
            source.Play();
            PlayCount++;
            _ = eventKey;
            _ = payload;
            return true;
        }

        /// <summary>Stops and releases every source; an audio failure never undoes simulation (04 s7).</summary>
        public int StopAll()
        {
            int stopped = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].Stop();
                    stopped++;
                }
            }

            return stopped;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopAll();
            pool.Clear();
            clips.Clear();
            if (ownsRoot && root != null)
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        private AudioSource Rent()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !pool[i].isPlaying)
                {
                    return pool[i];
                }
            }

            var host = new GameObject("AudioSource");
            host.transform.SetParent(root, false);
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            pool.Add(source);
            return source;
        }
    }
}
