// GameCore.Unity.Adapters.Input — the Unity device input source (GC-019).
//
// Normative sources: 04 s7's Input row ("Host samples UI/device input ... A sampled key press is not itself a
// committed game event. First slice uses test commands and ordinary Unity callbacks, so no Input System package pin
// is implied") and 04 s3 ("Presentation runs on host frames even when a command-driven world is idle").
//
// A source produces *readings*, one per pass, from the engine's own input API. It holds no command identity, no
// world and no authority: stamping, validation and admission belong to `TypedInputIngress`/the world's command port.
// Sampling is polled from the pump's input point rather than from a MonoBehaviour `Update`, so the adapter adds no
// second update path (04 s3).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using UnityEngine;

namespace GameCore.Unity.Adapters.Input
{
    /// <summary>
    /// Reads Unity's classic `Input` API for the declared key codes. The declared list is content: an undeclared key
    /// is never sampled, so a project cannot accidentally bind a debug key into gameplay (P-008).
    /// </summary>
    public sealed class UnityDeviceInputSource : IDeviceInputSource
    {
        private readonly KeyCode[] keys;
        private readonly List<DeviceInputSample> buffer = new List<DeviceInputSample>();

        /// <summary>When false, only held-key levels are reported once per key press (edge sampling, the default).</summary>
        public bool ReportHeldKeysEveryFrame { get; set; }

        public UnityDeviceInputSource(Id128 sourceId, IReadOnlyList<KeyCode>? keys)
        {
            if (sourceId.IsDefault)
            {
                throw new ArgumentException("An input source needs a stable identity (P-004).", nameof(sourceId));
            }

            SourceId = sourceId;
            var declared = new List<KeyCode>();
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    declared.Add(keys[i]);
                }
            }

            // Canonical numeric order, so the sampling order never depends on declaration order (P-008).
            declared.Sort((left, right) => ((int)left).CompareTo((int)right));
            this.keys = declared.ToArray();
        }

        public Id128 SourceId { get; }

        public int DeclaredKeyCount => keys.Length;

        public int SampleCount { get; private set; }

        public int EdgeCount { get; private set; }

        /// <summary>Set to true by a headless/unavailable environment; sampling then reports an empty list.</summary>
        public bool SuppressSampling { get; set; }

        public IReadOnlyList<DeviceInputSample> Sample()
        {
            SampleCount++;
            buffer.Clear();
            if (SuppressSampling)
            {
                return buffer;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                KeyCode code = keys[i];
                bool held = Input.GetKey(code);
                if (!held)
                {
                    continue;
                }

                bool edge = Input.GetKeyDown(code);
                if (!edge && !ReportHeldKeysEveryFrame)
                {
                    continue;
                }

                EdgeCount++;
                buffer.Add(new DeviceInputSample(InputDeviceKind.Button, (int)code, 1));
            }

            return buffer;
        }
    }
}
