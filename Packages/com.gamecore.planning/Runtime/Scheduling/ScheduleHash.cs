// GameCore.Planning - semantic schedule compilation (GC-009).
//
// Canonical plan hash of one compiled schedule (P-040: "Both levels and their access sets enter validation and the
// plan hash"). The hash covers only semantic inputs in canonical order: stage identities and versions, stage and
// system access sets, inner system edges, ordered stage edges, buffer bindings and deferred playback points. It
// excludes declaration order, registration timing, timestamps and object addresses (P-008, 05 section 4).
//
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>Canonical byte encoding of a compiled schedule, hashed with SHA-256.</summary>
    internal static class ScheduleHash
    {
        internal static ContentHash Compute(
            IReadOnlyList<ScheduleStage> stages,
            IReadOnlyList<ScheduleStageEdge> edges,
            IReadOnlyList<SchedulePlaybackPoint> playbackPoints,
            IReadOnlyList<BufferBinding> bufferBindings)
        {
            var writer = new ByteWriter(1024);

            writer.WriteInt32(stages.Count);
            for (int s = 0; s < stages.Count; s++)
            {
                ScheduleStage stage = stages[s];
                writer.WriteInt32(stage.StageIndex);
                writer.WriteId128(stage.Stage.Value);
                writer.WriteUInt32(stage.Version);
                writer.WriteId128(stage.OwnerPackage);
                writer.WriteInt32((int)stage.Affinity);

                writer.WriteInt32(stage.Access.Declarations.Count);
                for (int a = 0; a < stage.Access.Declarations.Count; a++)
                {
                    WriteAccess(writer, stage.Access.Declarations[a]);
                }

                writer.WriteInt32(stage.FactoryKeys.Count);
                for (int k = 0; k < stage.FactoryKeys.Count; k++)
                {
                    WriteFactoryKey(writer, stage.FactoryKeys[k]);
                }

                writer.WriteInt32(stage.ActivationMemberships.Count);
                for (int m = 0; m < stage.ActivationMemberships.Count; m++)
                {
                    writer.WriteId128(stage.ActivationMemberships[m]);
                }

                writer.WriteInt32(stage.PredecessorStages.Count);
                for (int p = 0; p < stage.PredecessorStages.Count; p++)
                {
                    writer.WriteInt32(stage.PredecessorStages[p]);
                }

                writer.WriteInt32(stage.Systems.Count);
                for (int i = 0; i < stage.Systems.Count; i++)
                {
                    ScheduleEntry entry = stage.Systems[i];
                    writer.WriteInt32(entry.DispatchIndex);
                    WriteFactoryKey(writer, entry.SystemKey);
                    writer.WriteInt32((int)entry.Multiplicity);
                    writer.WriteInt32(entry.Access.Declarations.Count);
                    for (int a = 0; a < entry.Access.Declarations.Count; a++)
                    {
                        WriteAccess(writer, entry.Access.Declarations[a]);
                    }

                    writer.WriteInt32(entry.PredecessorSystems.Count);
                    for (int p = 0; p < entry.PredecessorSystems.Count; p++)
                    {
                        WriteFactoryKey(writer, entry.PredecessorSystems[p]);
                    }
                }
            }

            writer.WriteInt32(edges.Count);
            for (int e = 0; e < edges.Count; e++)
            {
                ScheduleStageEdge edge = edges[e];
                writer.WriteInt32(edge.FromIndex);
                writer.WriteInt32(edge.ToIndex);
                writer.WriteInt32((int)edge.Kind);
                writer.WriteByte(edge.Required ? (byte)1 : (byte)0);
            }

            writer.WriteInt32(playbackPoints.Count);
            for (int p = 0; p < playbackPoints.Count; p++)
            {
                SchedulePlaybackPoint point = playbackPoints[p];
                writer.WriteInt32(point.PlaybackIndex);
                writer.WriteId128(point.Buffer.Value);
                writer.WriteId128(point.Schema.Id.Value);
                writer.WriteUInt32(point.Schema.Version);
                WriteFactoryKey(writer, point.OrderKey);
                writer.WriteInt32((int)point.Lifetime);
                writer.WriteInt32((int)point.Overflow);
                writer.WriteInt32((int)point.Cancellation);
                writer.WriteInt32(point.Capacity);
                writer.WriteInt32(point.OwnerStageIndex);
                writer.WriteInt32(point.ConsumerStageIndex);
                writer.WriteInt32(point.ProducerStageIndexes.Count);
                for (int i = 0; i < point.ProducerStageIndexes.Count; i++)
                {
                    writer.WriteInt32(point.ProducerStageIndexes[i]);
                }

                writer.WriteInt32(point.ProducerSystems.Count);
                for (int i = 0; i < point.ProducerSystems.Count; i++)
                {
                    WriteFactoryKey(writer, point.ProducerSystems[i]);
                }

                writer.WriteInt32(point.ConsumerSystems.Count);
                for (int i = 0; i < point.ConsumerSystems.Count; i++)
                {
                    WriteFactoryKey(writer, point.ConsumerSystems[i]);
                }
            }

            writer.WriteInt32(bufferBindings.Count);
            for (int b = 0; b < bufferBindings.Count; b++)
            {
                BufferBinding binding = bufferBindings[b];
                writer.WriteId128(binding.Buffer.Value);
                writer.WriteInt32(binding.Producers.Count);
                for (int i = 0; i < binding.Producers.Count; i++)
                {
                    WriteFactoryKey(writer, binding.Producers[i]);
                }

                writer.WriteId128(binding.ConsumerStage.Value);
            }

            return ContentHash.Compute(writer.ToArray());
        }

        private static void WriteAccess(ByteWriter writer, AccessDeclaration declaration)
        {
            writer.WriteId128(declaration.Schema.Id.Value);
            writer.WriteUInt32(declaration.Schema.Version);
            writer.WriteInt32((int)declaration.Mode);
            writer.WriteId128(declaration.PartitionId);
        }

        private static void WriteFactoryKey(ByteWriter writer, FactoryKey key)
        {
            writer.WriteId128(key.RegistrationKey);
            writer.WriteUInt32(key.KeyVersion);
        }

        private sealed class ByteWriter
        {
            private readonly List<byte> bytes;

            internal ByteWriter(int capacity)
            {
                bytes = new List<byte>(capacity);
            }

            internal void WriteByte(byte value) => bytes.Add(value);

            internal void WriteInt32(int value)
            {
                WriteUInt64((ulong)(uint)value);
            }

            internal void WriteUInt32(uint value)
            {
                WriteUInt64(value);
            }

            internal void WriteUInt64(ulong value)
            {
                for (int i = 0; i < 8; i++)
                {
                    bytes.Add((byte)(value >> ((7 - i) * 8)));
                }
            }

            internal void WriteId128(Id128 value)
            {
                WriteUInt64(value.High);
                WriteUInt64(value.Low);
            }

            internal byte[] ToArray() => bytes.ToArray();
        }
    }
}
