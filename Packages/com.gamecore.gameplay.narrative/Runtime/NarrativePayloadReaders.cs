// GameCore.Gameplay.Narrative — the generated-style payload readers of the narrative routes (04 section 8, P-042).
//
// IL2CPP forbids runtime type construction, so the executable type set and every payload reader are known at build
// time: a route resolves its reader through the plane's `CommandPayloadReaders` registry by schema identity, and a
// miss is an explicit refusal rather than a reflective attempt (P-009). Each reader below decodes exactly the bytes
// `NarrativePayloadCodec` writes and refuses anything else, so a malformed payload is a rejection a stage can report.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>Generated-style reader of the admitted choice command: two big-endian int32 scalars.</summary>
    public sealed class NarrativeChoicePayloadReader : ICommandPayloadReader<NarrativeChoice>
    {
        public SchemaRef Schema => NarrativeKeys.ChoiceCommandSchema;

        public NarrativeChoice Read(IReadOnlyList<byte> payload)
        {
            if (!NarrativePayloadCodec.TryDecodeChoice(payload, out NarrativeChoice choice))
            {
                throw new ArgumentException(
                    "the choice payload is exactly " + NarrativeChoice.EncodedLength
                    + " bytes of two big-endian int32 scalars.", nameof(payload));
            }

            return choice;
        }
    }

    /// <summary>Generated-style reader of the dialogue stage's quest-mutation request.</summary>
    public sealed class NarrativeMutationPayloadReader : ICommandPayloadReader<NarrativeMutation>
    {
        public SchemaRef Schema => NarrativeKeys.QuestMutationSchema;

        public NarrativeMutation Read(IReadOnlyList<byte> payload)
        {
            if (!NarrativePayloadCodec.TryDecodeMutation(payload, out NarrativeMutation mutation))
            {
                throw new ArgumentException(
                    "the mutation payload is exactly " + NarrativeMutation.EncodedLength
                    + " bytes of two big-endian int32 scalars.", nameof(payload));
            }

            return mutation;
        }
    }

    /// <summary>Generated-style reader of one committed fact observation (value and version).</summary>
    public sealed class NarrativeObservationPayloadReader : ICommandPayloadReader<NarrativeObservation>
    {
        public SchemaRef Schema => NarrativeKeys.FactObservedSchema;

        public NarrativeObservation Read(IReadOnlyList<byte> payload)
        {
            if (!NarrativePayloadCodec.TryDecodeObservation(payload, out NarrativeObservation observation))
            {
                throw new ArgumentException(
                    "the observation payload is exactly " + NarrativeObservation.EncodedLength
                    + " bytes of three big-endian int32 scalars.", nameof(payload));
            }

            return observation;
        }
    }
}
