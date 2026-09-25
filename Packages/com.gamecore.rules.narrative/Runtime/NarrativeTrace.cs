// GameCore.Rules.Narrative — the canonical narrative trace (deterministic evidence, P-008, P-060).
//
// One trace document records two things that must never be confused:
//
//   * the **rules** section, which is a pure function of this package's declarations. Any process that runs this
//     code — a plain dotnet test, the Unity Editor, an IL2CPP player — regenerates it byte for byte, so a committed
//     trace file is a real regression check on the rules rather than a recording nobody can reproduce;
//   * the **pipeline** section, which records numbers only a real run can produce (the composition publication's
//     revision/epoch, the derived layout, the committed command outcome). It is written by the run that observed
//     it, and the committed file carries the values the narrative slice requires; the Unity-world fixture asserts
//     its live facts equal them.
//
// The writer is deliberately hand-rolled rather than reflection- or serializer-based: 04 section 8 forbids runtime
// type discovery, and a fixed writer is what makes "the same input produces the same bytes" checkable by eye.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Rules.Narrative
{
    /// <summary>One recorded pipeline observation: a stable key and its canonical text value.</summary>
    public readonly struct NarrativeTraceEntry
    {
        public NarrativeTraceEntry(string key, string value)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value ?? string.Empty;
        }

        public string Key { get; }

        /// <summary>Canonical text of the observed value; a number, a boolean or a stable name, never prose.</summary>
        public string Value { get; }

        public override string ToString() => Key + "=" + Value;
    }

    /// <summary>Deterministic writer and reader of the one canonical narrative trace document.</summary>
    public static class NarrativeTrace
    {
        /// <summary>Format identity of the document this writer emits.</summary>
        public const string TraceFormat = "gamecore.narrative-trace/1";

        /// <summary>Protocol version this trace belongs to.</summary>
        public const string ProtocolVersion = "1.0";

        /// <summary>Task identity the trace is evidence for.</summary>
        public const string Task = "GC-010";

        /// <summary>File name the trace is committed under.</summary>
        public const string FileName = "narrative-trace.json";

        private const string Indent = "  ";

        /// <summary>
        /// The rules section alone, as canonical lines. It is a pure function of this package's declarations, so a
        /// test can compare it against a committed recording without running a world.
        /// </summary>
        public static IReadOnlyList<string> RulesSectionLines()
        {
            var lines = new List<string>();

            // Chapters and the content each one binds.
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                lines.Add(
                    "chapter=" + chapter.ChapterTag
                    + ";ordinal=" + chapter.BindingOrdinal.ToString(CultureInfo.InvariantCulture)
                    + ";openingNode=" + chapter.OpeningNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                    + ";factKey=" + chapter.GateConditionFactKey
                    + ";dialogueGraph=" + chapter.DialogueGraphDefinition
                    + ";gateCondition=" + chapter.GateConditionDefinition
                    + ";choiceSurface=" + chapter.ChoiceSurfaceDefinition
                    + ";hooks=" + Join(chapter.EncounterHookPlan));

                IReadOnlyList<string> bindings = NarrativeDerivationPlan.CanonicalLines(chapter.ChapterTag);
                for (int b = 0; b < bindings.Count; b++)
                {
                    lines.Add("chapter=" + chapter.ChapterTag + ";" + bindings[b]);
                }
            }

            // Fact transitions: the declared initiation and the one accepted permit transition.
            lines.Add(FactLine(NarrativeFacts.BridgePermitFactKey, NarrativeFacts.InitialValue, NarrativeFacts.False));
            lines.Add(FactLine(NarrativeFacts.BridgePermitFactKey, NarrativeFacts.InitialValue, NarrativeFacts.True));
            lines.Add(FactLine(NarrativeFacts.HarborPermitFactKey, NarrativeFacts.InitialValue, NarrativeFacts.True));

            // Dialogue decisions: the accepted permit, the accepted decline and one refusal of each declared kind.
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 1, 1, NarrativeConversationStatus.Idle));
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 1, 2, NarrativeConversationStatus.Idle));
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 1, 1, NarrativeConversationStatus.Active));
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 2, 1, NarrativeConversationStatus.Idle));
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 1, 3, NarrativeConversationStatus.Idle));
            lines.Add(DialogueLine(NarrativeChapters.ChapterOneTag, 1, 1, NarrativeConversationStatus.Closed));

            // Gate decisions: closed under the initial fact, open under the permit at the transition's version.
            lines.Add(GateLine(NarrativeFacts.False, NarrativeFacts.InitialVersion));
            lines.Add(GateLine(NarrativeFacts.True, NarrativeFacts.InitialVersion + 1));

            // Encounter reactions: idle stays idle without the condition, becomes active with it, completes on loss.
            lines.Add(EncounterLine(NarrativeEncounterStatus.Idle, false));
            lines.Add(EncounterLine(NarrativeEncounterStatus.Idle, true));
            lines.Add(EncounterLine(NarrativeEncounterStatus.Active, false));

            // Conversation transitions, including the two the declared table refuses.
            lines.Add(ConversationLine(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Requested));
            lines.Add(ConversationLine(NarrativeConversationStatus.Requested, NarrativeConversationStatus.Active));
            lines.Add(ConversationLine(NarrativeConversationStatus.Active, NarrativeConversationStatus.Closed));
            lines.Add(ConversationLine(NarrativeConversationStatus.Active, NarrativeConversationStatus.Requested));
            lines.Add(ConversationLine(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Closed));
            lines.Add(ConversationLine(NarrativeConversationStatus.Closed, NarrativeConversationStatus.Requested));

            // The registered migration: a zero node initializes to the chapter's opening node, a negative refuses.
            lines.Add("migration.conversation.source=0;accepted=true;migrated="
                + NarrativeChapters.Get(NarrativeChapters.ChapterOneTag).OpeningNodeOrdinal.ToString(CultureInfo.InvariantCulture));
            lines.Add("migration.conversation.source=-1;accepted=false;refusal=" + NarrativeRefusals.MigrationSourceRefused);

            return lines;
        }

        /// <summary>The digest of the rules section, so two runs can be compared with one value.</summary>
        public static string RulesDigest() => NarrativeDigest.OfLines(RulesSectionLines());

        /// <summary>
        /// Writes the whole trace document: the rules section above, the neutrality audit over the registered names
        /// the caller supplies, and the pipeline observations in the order they are given.
        /// </summary>
        public static string Write(
            IReadOnlyList<string>? registeredNames,
            IReadOnlyList<NarrativeTraceEntry>? pipeline)
        {
            GenreAuditReport audit = NarrativeGenreAudit.Audit(registeredNames ?? Array.Empty<string>());

            var text = new StringBuilder();
            text.Append("{\n");
            text.Append(Indent).Append("\"traceFormat\": \"").Append(TraceFormat).Append("\",\n");
            text.Append(Indent).Append("\"protocolVersion\": \"").Append(ProtocolVersion).Append("\",\n");
            text.Append(Indent).Append("\"task\": \"").Append(Task).Append("\",\n");
            text.Append(Indent).Append("\"determinism\": \"")
                .Append("the rules section and the registered-name digest are pure functions of the narrative package's"
                    + " declarations; the pipeline section records one real run's numbers (P-008, P-060)")
                .Append("\",\n");

            // Rules.
            text.Append(Indent).Append("\"rules\": [\n");
            IReadOnlyList<string> rules = RulesSectionLines();
            for (int i = 0; i < rules.Count; i++)
            {
                text.Append(Indent).Append(Indent).Append('"').Append(Escape(rules[i])).Append('"');
                text.Append(i + 1 < rules.Count ? ",\n" : "\n");
            }

            text.Append(Indent).Append("],\n");
            text.Append(Indent).Append("\"rulesDigest\": \"").Append(RulesDigest()).Append("\",\n");

            // Genres.
            text.Append(Indent).Append("\"genres\": {\n");
            text.Append(Indent).Append(Indent).Append("\"checked\": ")
                .Append(audit.CheckedCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append(Indent).Append(Indent).Append("\"neutral\": ")
                .Append(audit.Neutral ? "true" : "false").Append(",\n");
            text.Append(Indent).Append(Indent).Append("\"forbiddenCount\": ")
                .Append(audit.ForbiddenNames.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append(Indent).Append(Indent).Append("\"registeredNames\": [\n");
            for (int i = 0; i < registeredNames!.Count; i++)
            {
                text.Append(Indent).Append(Indent).Append(Indent).Append('"')
                    .Append(Escape(registeredNames[i])).Append('"');
                text.Append(i + 1 < registeredNames.Count ? ",\n" : "\n");
            }

            text.Append(Indent).Append(Indent).Append("],\n");
            text.Append(Indent).Append(Indent).Append("\"registeredNamesDigest\": \"")
                .Append(NarrativeDigest.OfLines(registeredNames)).Append("\"\n");
            text.Append(Indent).Append("},\n");

            // Pipeline.
            text.Append(Indent).Append("\"pipeline\": {\n");
            int count = pipeline == null ? 0 : pipeline.Count;
            for (int i = 0; i < count; i++)
            {
                text.Append(Indent).Append(Indent).Append('"').Append(Escape(pipeline![i].Key)).Append("\": \"")
                    .Append(Escape(pipeline[i].Value)).Append('"');
                text.Append(i + 1 < count ? ",\n" : "\n");
            }

            text.Append(Indent).Append("}\n");
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>
        /// Reads the pipeline entries back out of a committed trace document, so a pure test can regenerate the
        /// whole document from them and require byte equality with what is committed.
        /// </summary>
        public static bool TryReadPipelineEntries(string documentText, out IReadOnlyList<NarrativeTraceEntry> entries)
        {
            entries = Array.Empty<NarrativeTraceEntry>();
            if (documentText == null)
            {
                throw new ArgumentNullException(nameof(documentText));
            }

            const string PipelineHeader = "  \"pipeline\": {";
            string[] lines = documentText.Replace("\r\n", "\n").Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i], PipelineHeader, StringComparison.Ordinal))
                {
                    start = i + 1;
                    break;
                }
            }

            if (start < 0)
            {
                return false;
            }

            var found = new List<NarrativeTraceEntry>();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("  }", StringComparison.Ordinal))
                {
                    entries = found;
                    return true;
                }

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.EndsWith(",", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 1);
                }

                int separator = trimmed.IndexOf("\": \"", StringComparison.Ordinal);
                if (!trimmed.StartsWith("\"", StringComparison.Ordinal) || separator < 0
                    || !trimmed.EndsWith("\"", StringComparison.Ordinal))
                {
                    // A shape this reader does not understand is a refusal, never a partially read trace.
                    entries = Array.Empty<NarrativeTraceEntry>();
                    return false;
                }

                string key = Unescape(trimmed.Substring(1, separator - 1));
                string value = Unescape(trimmed.Substring(separator + 4, trimmed.Length - separator - 5));
                found.Add(new NarrativeTraceEntry(key, value));
            }

            entries = Array.Empty<NarrativeTraceEntry>();
            return false;
        }


        private static string ConversationLine(int currentStatus, int requestedStatus)
        {
            bool accepted = NarrativeDialogueRules.TryTransition(
                currentStatus,
                requestedStatus,
                out int nextStatus,
                out string refusalCode);

            return "conversation.from=" + currentStatus.ToString(CultureInfo.InvariantCulture)
                + ";requested=" + requestedStatus.ToString(CultureInfo.InvariantCulture)
                + ";accepted=" + (accepted ? "true" : "false")
                + ";to=" + nextStatus.ToString(CultureInfo.InvariantCulture)
                + ";refusal=" + (accepted ? "<none>" : refusalCode);
        }
        private static string FactLine(string factKey, int currentValue, int requestedValue)
        {
            bool accepted = NarrativeFacts.TryTransition(
                currentValue,
                requestedValue,
                out int nextValue,
                out string refusalCode);

            return "fact=" + factKey
                + ";from=" + currentValue.ToString(CultureInfo.InvariantCulture)
                + ";requested=" + requestedValue.ToString(CultureInfo.InvariantCulture)
                + ";accepted=" + (accepted ? "true" : "false")
                + ";to=" + nextValue.ToString(CultureInfo.InvariantCulture)
                + ";fromVersion=" + NarrativeFacts.InitialVersion.ToString(CultureInfo.InvariantCulture)
                + ";toVersion=" + (accepted
                    ? NarrativeFacts.NextVersion(NarrativeFacts.InitialVersion).ToString(CultureInfo.InvariantCulture)
                    : NarrativeFacts.InitialVersion.ToString(CultureInfo.InvariantCulture))
                + ";refusal=" + (accepted ? "<none>" : refusalCode);
        }

        private static string DialogueLine(string chapterTag, int node, int choice, int currentStatus)
        {
            ChapterDefinition chapter = NarrativeChapters.Get(chapterTag);
            ChoiceValidation validation = NarrativeDialogueRules.Validate(
                chapter,
                new NarrativeChoice(node, choice),
                chapter.OpeningNodeOrdinal,
                currentStatus);

            return "dialogue=" + chapterTag
                + ";status=" + currentStatus.ToString(CultureInfo.InvariantCulture)
                + ";node=" + node.ToString(CultureInfo.InvariantCulture)
                + ";choice=" + choice.ToString(CultureInfo.InvariantCulture)
                + ";accepted=" + (validation.Accepted ? "true" : "false")
                + ";resultingNode=" + validation.ResultingNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                + ";resultingStatus=" + validation.ResultingStatus.ToString(CultureInfo.InvariantCulture)
                + ";factKey=" + (validation.RequestsFactMutation ? validation.FactKey : "<none>")
                + ";factValue=" + validation.FactValue.ToString(CultureInfo.InvariantCulture)
                + ";refusal=" + (validation.Accepted ? "<none>" : validation.RefusalCode);
        }

        private static string GateLine(int factValue, int factVersion)
        {
            bool accepted = NarrativeGateRules.TryEvaluate(
                factValue,
                factVersion,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            return "gate=fact" + factValue.ToString(CultureInfo.InvariantCulture)
                + ";factVersion=" + factVersion.ToString(CultureInfo.InvariantCulture)
                + ";accepted=" + (accepted ? "true" : "false")
                + ";decision=" + decision.ToString(CultureInfo.InvariantCulture)
                + ";evaluatedFactVersion=" + evaluatedFactVersion.ToString(CultureInfo.InvariantCulture)
                + ";refusal=" + (accepted ? "<none>" : refusalCode);
        }

        private static string EncounterLine(int status, bool conditionHolds)
        {
            bool accepted = NarrativeEncounterRules.TryReactToCondition(
                status,
                conditionHolds,
                out int nextStatus,
                out string refusalCode);

            return "encounter=status" + status.ToString(CultureInfo.InvariantCulture)
                + ";condition=" + (conditionHolds ? "true" : "false")
                + ";accepted=" + (accepted ? "true" : "false")
                + ";next=" + nextStatus.ToString(CultureInfo.InvariantCulture)
                + ";refusal=" + (accepted ? "<none>" : refusalCode);
        }

        private static string Join(IReadOnlyList<string> values)
        {
            var text = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i != 0)
                {
                    text.Append('|');
                }

                text.Append(values[i]);
            }

            return text.ToString();
        }

        private static string Escape(string value)
        {
            var text = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' || c == '"')
                {
                    text.Append('\\').Append(c);
                }
                else if (c == '\n')
                {
                    text.Append("\\n");
                }
                else if (c == '\t')
                {
                    text.Append("\\t");
                }
                else
                {
                    text.Append(c);
                }
            }

            return text.ToString();
        }

        private static string Unescape(string value)
        {
            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    text.Append(c);
                    continue;
                }

                i++;
                char escaped = value[i];
                if (escaped == 'n')
                {
                    text.Append('\n');
                }
                else if (escaped == 't')
                {
                    text.Append('\t');
                }
                else
                {
                    text.Append(escaped);
                }
            }

            return text.ToString();
        }
    }
}
