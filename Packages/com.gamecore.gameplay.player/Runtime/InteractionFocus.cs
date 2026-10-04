// GameCore.Gameplay.Player - InteractionFocus: ranked focus candidates, player.setFocus and the prompt (P1.3).
//
// Before the pump (an IGameplayInputSource) it gathers the focus candidates of every registered IInteractableSource
// (the npc and interaction runtimes provide them), keeps those in the player's region, ranks them with the pure
// FocusRules (priority, then distance + angle score, then key; hysteresis for the current focus) from the COMMITTED
// player pose, and submits player.setFocus when the winner differs from the committed focus. Candidate positions are
// committed slots too, so focus is the same headless and under replay; a physics sphere cast is not used because it
// would make focus depend on presentation (views do not exist headless). After the pump (an IPresentationBinder) it
// shows the committed focus's prompt through IPromptPresenter, or hides it.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;

namespace GameCore.Gameplay.Player
{
    /// <summary>The player's interaction focus.</summary>
    public sealed class InteractionFocus : IGameplayInputSource, IPresentationBinder
    {
        private readonly List<IInteractableSource> sources = new List<IInteractableSource>();
        private readonly List<IInteractable> candidates = new List<IInteractable>();
        private readonly List<FocusCandidate> ranked = new List<FocusCandidate>();
        private readonly PlayerWorldExtension extension;
        private readonly PlayerCommands commands;
        private int shownKey = PlayerRules.NoFocus;
        private int shownState = int.MinValue;

        public InteractionFocus(PlayerWorldExtension extension, PlayerCommands commands, FocusTuning tuning)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Tuning = tuning;
        }

        public string BinderName => "gameplay.interaction-focus";

        /// <summary>No engine object is touched: the prompt goes through the presenter interface.</summary>
        public bool IsActive => true;

        public bool Enabled { get; set; } = true;

        public FocusTuning Tuning { get; set; }

        public IPromptPresenter Prompts { get; set; } = new NullPromptPresenter();

        public int Submitted { get; private set; }

        /// <summary>The key the last ranking chose (NoFocus when nothing is in range).</summary>
        public int LastRanked { get; private set; } = PlayerRules.NoFocus;

        public void AddSource(IInteractableSource source) => sources.Add(source ?? throw new ArgumentNullException(nameof(source)));

        /// <summary>The candidate with this key, if a source offers it.</summary>
        public IInteractable? Find(int key)
        {
            candidates.Clear();
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Collect(candidates);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Key == key)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        public int Collect(GameplayWorld world)
        {
            if (!Enabled || extension.Module == null)
            {
                return 0;
            }

            var player = extension.Player;
            if (!world.Slots.TryRead(player, PlayerSlots.Owner, PlayerSlots.PosX, out int px)
                || !world.Slots.TryRead(player, PlayerSlots.Owner, PlayerSlots.PosZ, out int pz))
            {
                return 0;
            }

            int yaw = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.Yaw, 0);
            int region = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.RegionKey, 0);
            int focus = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.Focus, PlayerRules.NoFocus);
            candidates.Clear();
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Collect(candidates);
            }

            ranked.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                IInteractable candidate = candidates[i];
                if (candidate.Key <= 0 || !candidate.TryGetFocusPoint(world.Slots, out int x, out int _, out int z, out int candidateRegion)
                    || candidateRegion != region)
                {
                    continue;
                }

                ranked.Add(new FocusCandidate(candidate.Key, PlanarMath.Sub(x, px), PlanarMath.Sub(z, pz), candidate.Priority));
            }

            int chosen = FocusRules.Rank(ranked, yaw, Tuning, focus);
            LastRanked = chosen;
            if (chosen == focus)
            {
                return 0;
            }

            if (commands.SetFocus(chosen).Admitted)
            {
                Submitted++;
                return 1;
            }

            return 0;
        }

        public int Present(ICommittedSlotReader slots)
        {
            int focus = slots.TryRead(extension.Player, PlayerSlots.Owner, PlayerSlots.Focus, out int value) ? value : PlayerRules.NoFocus;
            if (focus == PlayerRules.NoFocus)
            {
                if (shownKey != PlayerRules.NoFocus)
                {
                    Prompts.Hide();
                    shownKey = PlayerRules.NoFocus;
                    shownState = int.MinValue;
                    return 1;
                }

                return 0;
            }

            IInteractable? candidate = Find(focus);
            if (candidate == null)
            {
                return 0;
            }

            PromptRequest prompt = candidate.PromptFor(slots);
            if (focus == shownKey && prompt.State == shownState)
            {
                return 0;
            }

            Prompts.Show(prompt);
            shownKey = focus;
            shownState = prompt.State;
            return 1;
        }
    }
}
