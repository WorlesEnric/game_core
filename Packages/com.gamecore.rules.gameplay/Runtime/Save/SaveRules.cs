// GameCore.Rules.Gameplay - the pure save rules of a game (plugin catalog row 12, SADR-012 (studio)).
//
// The kernel decides whether a world *can* be captured or restored (a committed boundary, not faulted, not stopping).
// A game decides whether it *may*: no save during a cutscene, a boss fight or a scene transition, which slot names a
// player sees, how many manual slots exist and which slot the next autosave overwrites. Those decisions are gameplay
// rules, so they live here, pure and engine-free (P-057), and the save plugin consults them before it asks the
// application root's save service to act.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Gameplay.Save
{
    /// <summary>Gameplay states during which a save or a load is not offered (flags).</summary>
    [Flags]
    public enum SaveGateState
    {
        None = 0,
        Cutscene = 1,
        Combat = 2,
        SceneTransition = 4,
        Dialogue = 8,
        /// <summary>A game-specific state the game names itself.</summary>
        Custom = 16,
    }

    /// <summary>The verdict of one save gate decision.</summary>
    public readonly struct SaveGateVerdict
    {
        public SaveGateVerdict(bool allowed, string codeId, string hint)
        {
            Allowed = allowed;
            CodeId = codeId ?? string.Empty;
            Hint = hint ?? string.Empty;
        }

        public bool Allowed { get; }

        /// <summary>The stable refusal id (the save service's `save.unsafe-state`), empty when allowed.</summary>
        public string CodeId { get; }

        public string Hint { get; }

        public override string ToString() => Allowed ? "allowed" : CodeId + ": " + Hint;
    }

    /// <summary>
    /// Which gameplay states block a capture and which block a restore. Defaults: a capture is blocked in every listed
    /// state, a restore only during a scene transition (a player may load out of a fight or a cutscene).
    /// </summary>
    public sealed class SaveGateRules
    {
        public const string UnsafeStateCode = "save.unsafe-state";

        public SaveGateRules(SaveGateState blocksCapture, SaveGateState blocksRestore)
        {
            BlocksCapture = blocksCapture;
            BlocksRestore = blocksRestore;
        }

        public static SaveGateRules Default { get; } = new SaveGateRules(
            SaveGateState.Cutscene | SaveGateState.Combat | SaveGateState.SceneTransition | SaveGateState.Dialogue | SaveGateState.Custom,
            SaveGateState.SceneTransition | SaveGateState.Custom);

        public SaveGateState BlocksCapture { get; }

        public SaveGateState BlocksRestore { get; }

        public SaveGateVerdict MayCapture(SaveGateState current) => Decide(current & BlocksCapture, "saving");

        public SaveGateVerdict MayRestore(SaveGateState current) => Decide(current & BlocksRestore, "loading");

        private static SaveGateVerdict Decide(SaveGateState blocking, string action)
        {
            if (blocking == SaveGateState.None)
            {
                return new SaveGateVerdict(true, string.Empty, string.Empty);
            }

            return new SaveGateVerdict(false, UnsafeStateCode, action + " is not available during " + Describe(blocking));
        }

        private static string Describe(SaveGateState state)
        {
            var parts = new List<string>();
            if ((state & SaveGateState.Cutscene) != 0)
            {
                parts.Add("a cutscene");
            }

            if ((state & SaveGateState.Combat) != 0)
            {
                parts.Add("combat");
            }

            if ((state & SaveGateState.SceneTransition) != 0)
            {
                parts.Add("a scene transition");
            }

            if ((state & SaveGateState.Dialogue) != 0)
            {
                parts.Add("a dialogue");
            }

            if ((state & SaveGateState.Custom) != 0)
            {
                parts.Add("the current game state");
            }

            return string.Join(" or ", parts.ToArray());
        }
    }

    /// <summary>
    /// The player-facing slot layout: one autosave rotation, one quicksave and a bounded number of manual slots. Slot
    /// names are the save service's file stems (lowercase a-z, 0-9, '-', '_').
    /// </summary>
    public sealed class SaveSlotPolicy
    {
        public const string QuickSlot = "quick";
        public const string AutoPrefix = "auto-";
        public const string ManualPrefix = "slot-";

        public SaveSlotPolicy(int manualSlots, int autosaveRotation)
        {
            if (manualSlots < 0 || manualSlots > 999)
            {
                throw new ArgumentOutOfRangeException(nameof(manualSlots), "Manual slots are 0..999.");
            }

            if (autosaveRotation < 1 || autosaveRotation > 99)
            {
                throw new ArgumentOutOfRangeException(nameof(autosaveRotation), "An autosave rotation has 1..99 slots.");
            }

            ManualSlots = manualSlots;
            AutosaveRotation = autosaveRotation;
        }

        public static SaveSlotPolicy Default { get; } = new SaveSlotPolicy(10, 3);

        public int ManualSlots { get; }

        public int AutosaveRotation { get; }

        public string ManualSlot(int index)
        {
            if (index < 1 || index > ManualSlots)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Manual slots are numbered 1.." + ManualSlots.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return ManualPrefix + index.ToString(CultureInfo.InvariantCulture);
        }

        public string AutoSlot(int index)
        {
            if (index < 1 || index > AutosaveRotation)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Autosave slots are numbered 1.." + AutosaveRotation.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return AutoPrefix + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>True for a slot this layout offers.</summary>
        public bool IsOffered(string? slot)
        {
            if (string.IsNullOrEmpty(slot))
            {
                return false;
            }

            if (string.Equals(slot, QuickSlot, StringComparison.Ordinal))
            {
                return true;
            }

            return InRange(slot!, ManualPrefix, ManualSlots) || InRange(slot!, AutoPrefix, AutosaveRotation);
        }

        /// <summary>
        /// The autosave slot the next autosave overwrites: the first unused one, otherwise the one with the oldest
        /// timestamp; ties go to the lowest index, so the choice is deterministic.
        /// </summary>
        public string NextAutosave(IReadOnlyDictionary<string, DateTime>? existing)
        {
            string oldest = AutoSlot(1);
            DateTime oldestAt = DateTime.MaxValue;
            for (int i = 1; i <= AutosaveRotation; i++)
            {
                string slot = AutoSlot(i);
                if (existing == null || !existing.TryGetValue(slot, out DateTime at))
                {
                    return slot;
                }

                if (at < oldestAt)
                {
                    oldestAt = at;
                    oldest = slot;
                }
            }

            return oldest;
        }

        private static bool InRange(string slot, string prefix, int count)
        {
            if (!slot.StartsWith(prefix, StringComparison.Ordinal) || slot.Length == prefix.Length || slot.Length > prefix.Length + 3)
            {
                return false;
            }

            string digits = slot.Substring(prefix.Length);
            if (digits[0] == '0')
            {
                return false;
            }

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                {
                    return false;
                }
            }

            int index = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
            return index >= 1 && index <= count;
        }
    }
}
