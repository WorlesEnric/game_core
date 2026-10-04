// GameCore.Rules.Gameplay.Npc - pure NPC behaviour: the state machine, patrol stepping, approach, conversation hold
// and schedule phases (P1.3, catalog row 4).
//
// An NPC's authoritative state is int32 slots owned by the npc plugin: npc.state (idle/patrol/approach/converse/custom),
// npc.behaviour (the standing behaviour the state returns to), npc.patrolIndex, npc.mood, npc.schedulePhase,
// npc.targetX/Z (mm), the logical pose npc.posX/Z (mm) and npc.yaw (mrad), and npc.timerMs (wait/hold countdown).
// Movement is integer stepping along straight segments between patrol points: every NPC moves logically whether its
// region is loaded or not; an unloaded region's NPCs are stepped less often with a proportionally longer elapsed time
// (same average speed, coarser path). Presentation (NavMesh agents, animators) only follows these slots.
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Player;

namespace GameCore.Rules.Gameplay.Npc
{
    /// <summary>Values of the npc.state and npc.behaviour slots.</summary>
    public enum NpcStateCode
    {
        Idle = 0,
        Patrol = 1,
        Approach = 2,
        Converse = 3,
        Custom = 4,
    }

    /// <summary>The NPC's authoritative slots.</summary>
    public readonly struct NpcSnapshot
    {
        public NpcSnapshot(
            int state,
            int behaviour,
            int patrolIndex,
            int mood,
            int schedulePhase,
            int targetX,
            int targetZ,
            int posX,
            int posZ,
            int yaw,
            int timerMilliseconds)
        {
            State = state;
            Behaviour = behaviour;
            PatrolIndex = patrolIndex;
            Mood = mood;
            SchedulePhase = schedulePhase;
            TargetX = targetX;
            TargetZ = targetZ;
            PosX = posX;
            PosZ = posZ;
            Yaw = yaw;
            TimerMilliseconds = timerMilliseconds;
        }

        public int State { get; }

        public int Behaviour { get; }

        public int PatrolIndex { get; }

        public int Mood { get; }

        public int SchedulePhase { get; }

        public int TargetX { get; }

        public int TargetZ { get; }

        public int PosX { get; }

        public int PosZ { get; }

        public int Yaw { get; }

        public int TimerMilliseconds { get; }

        public NpcStateCode StateCode => (NpcStateCode)State;

        public NpcSnapshot With(
            int? state = null,
            int? behaviour = null,
            int? patrolIndex = null,
            int? mood = null,
            int? schedulePhase = null,
            int? targetX = null,
            int? targetZ = null,
            int? posX = null,
            int? posZ = null,
            int? yaw = null,
            int? timer = null) =>
            new NpcSnapshot(
                state ?? State,
                behaviour ?? Behaviour,
                patrolIndex ?? PatrolIndex,
                mood ?? Mood,
                schedulePhase ?? SchedulePhase,
                targetX ?? TargetX,
                targetZ ?? TargetZ,
                posX ?? PosX,
                posZ ?? PosZ,
                yaw ?? Yaw,
                timer ?? TimerMilliseconds);

        public override string ToString() =>
            "npc(state=" + State + " behaviour=" + Behaviour + " patrol=" + PatrolIndex + " pos=" + PosX + "," + PosZ
            + " target=" + TargetX + "," + TargetZ + " timer=" + TimerMilliseconds + " phase=" + SchedulePhase + ")";
    }

    /// <summary>One patrol point in millimetres.</summary>
    public readonly struct PatrolPoint
    {
        public PatrolPoint(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }

        public int Z { get; }
    }

    /// <summary>Integer movement and timing profile of one NPC.</summary>
    public readonly struct NpcProfile
    {
        public NpcProfile(int speedMillimetresPerSecond, int waitMilliseconds, int converseMilliseconds, int arriveRadiusMillimetres)
        {
            SpeedMillimetresPerSecond = speedMillimetresPerSecond < 0 ? 0 : speedMillimetresPerSecond;
            WaitMilliseconds = waitMilliseconds < 0 ? 0 : waitMilliseconds;
            ConverseMilliseconds = converseMilliseconds < 0 ? 0 : converseMilliseconds;
            ArriveRadiusMillimetres = arriveRadiusMillimetres < 0 ? 0 : arriveRadiusMillimetres;
        }

        public int SpeedMillimetresPerSecond { get; }

        /// <summary>How long the NPC waits at each patrol point.</summary>
        public int WaitMilliseconds { get; }

        /// <summary>How long a conversation holds the NPC when no dialogue system ends it earlier.</summary>
        public int ConverseMilliseconds { get; }

        /// <summary>Distance at which a target counts as reached.</summary>
        public int ArriveRadiusMillimetres { get; }

        /// <summary>1.8 m/s, 1.5 s wait, 4 s conversation hold, 50 mm arrival radius.</summary>
        public static NpcProfile Default => new NpcProfile(1800, 1500, 4000, 50);
    }

    /// <summary>Why an NPC command was refused.</summary>
    public enum NpcRefusal
    {
        None = 0,
        UnknownBehaviour = 1,
        NoPatrolRoute = 2,
        Unchanged = 3,
        Conversing = 4,
        MoodOutOfRange = 5,
    }

    /// <summary>Stable refusal codes of the NPC rules.</summary>
    public static class NpcRefusals
    {
        public const string UnknownBehaviour = "npc.unknown-behaviour";
        public const string NoPatrolRoute = "npc.no-patrol-route";
        public const string Unchanged = "npc.unchanged";
        public const string Conversing = "npc.conversing";
        public const string MoodOutOfRange = "npc.mood-out-of-range";

        public static string Code(NpcRefusal refusal)
        {
            switch (refusal)
            {
                case NpcRefusal.UnknownBehaviour: return UnknownBehaviour;
                case NpcRefusal.NoPatrolRoute: return NoPatrolRoute;
                case NpcRefusal.Unchanged: return Unchanged;
                case NpcRefusal.Conversing: return Conversing;
                case NpcRefusal.MoodOutOfRange: return MoodOutOfRange;
                default: return string.Empty;
            }
        }
    }

    /// <summary>The result of one NPC transition or step.</summary>
    public readonly struct NpcTransition
    {
        public NpcTransition(NpcRefusal refusal, NpcSnapshot state, bool stateChanged, bool arrived, int arrivedIndex)
        {
            Refusal = refusal;
            State = state;
            StateChanged = stateChanged;
            Arrived = arrived;
            ArrivedIndex = arrivedIndex;
        }

        public NpcRefusal Refusal { get; }

        public NpcSnapshot State { get; }

        public bool Accepted => Refusal == NpcRefusal.None;

        /// <summary>npc.state differs from the input state (an NpcStateChanged event).</summary>
        public bool StateChanged { get; }

        /// <summary>A patrol point or a goTo target was reached in this step (an NpcArrived event).</summary>
        public bool Arrived { get; }

        /// <summary>The patrol index reached, or -1 for a goTo target.</summary>
        public int ArrivedIndex { get; }

        public static NpcTransition Refuse(NpcRefusal refusal, NpcSnapshot state) => new NpcTransition(refusal, state, false, false, -1);

        public static NpcTransition Accept(NpcSnapshot before, NpcSnapshot after) =>
            new NpcTransition(NpcRefusal.None, after, before.State != after.State, false, -1);
    }

    /// <summary>Pure NPC transitions.</summary>
    public static class NpcRules
    {
        public const int MoodMin = -100;

        public const int MoodMax = 100;

        /// <summary>An NPC placed at a pose with a standing behaviour; a patrol starts toward point 0.</summary>
        public static NpcSnapshot Placed(int x, int z, int yaw, NpcStateCode behaviour, IReadOnlyList<PatrolPoint> route, int mood)
        {
            int state = (int)behaviour;
            int targetX = x;
            int targetZ = z;
            if (behaviour == NpcStateCode.Patrol)
            {
                if (route != null && route.Count > 0)
                {
                    targetX = route[0].X;
                    targetZ = route[0].Z;
                }
                else
                {
                    state = (int)NpcStateCode.Idle;
                }
            }

            return new NpcSnapshot(state, state, 0, ClampMood(mood), 0, targetX, targetZ, x, z, PlanarMath.NormalizeYaw(yaw), 0);
        }

        /// <summary>True when <paramref name="behaviour"/> is a standing behaviour (idle, patrol or custom).</summary>
        public static bool IsStandingBehaviour(int behaviour) =>
            behaviour == (int)NpcStateCode.Idle || behaviour == (int)NpcStateCode.Patrol || behaviour == (int)NpcStateCode.Custom;

        /// <summary>npc.setBehaviour: changes the standing behaviour; a conversation keeps holding the NPC.</summary>
        public static NpcTransition SetBehaviour(NpcSnapshot state, int behaviour, IReadOnlyList<PatrolPoint> route)
        {
            if (!IsStandingBehaviour(behaviour))
            {
                return NpcTransition.Refuse(NpcRefusal.UnknownBehaviour, state);
            }

            if (behaviour == (int)NpcStateCode.Patrol && (route == null || route.Count == 0))
            {
                return NpcTransition.Refuse(NpcRefusal.NoPatrolRoute, state);
            }

            if (state.Behaviour == behaviour && state.State == behaviour)
            {
                return NpcTransition.Refuse(NpcRefusal.Unchanged, state);
            }

            if (state.StateCode == NpcStateCode.Converse || state.StateCode == NpcStateCode.Approach)
            {
                return NpcTransition.Accept(state, state.With(behaviour: behaviour));
            }

            return NpcTransition.Accept(state, Resume(state.With(behaviour: behaviour, timer: 0), route));
        }

        /// <summary>npc.goTo: walk to a point, then return to the standing behaviour (NpcArrived on arrival).</summary>
        public static NpcTransition GoTo(NpcSnapshot state, int x, int z)
        {
            if (state.StateCode == NpcStateCode.Converse)
            {
                return NpcTransition.Refuse(NpcRefusal.Conversing, state);
            }

            return NpcTransition.Accept(state, state.With(state: (int)NpcStateCode.Approach, targetX: x, targetZ: z, timer: 0));
        }

        /// <summary>npc.face: turn toward a point; position and state are unchanged.</summary>
        public static NpcTransition Face(NpcSnapshot state, int x, int z)
        {
            int dx = PlanarMath.Sub(x, state.PosX);
            int dz = PlanarMath.Sub(z, state.PosZ);
            if (dx == 0 && dz == 0)
            {
                return NpcTransition.Refuse(NpcRefusal.Unchanged, state);
            }

            return NpcTransition.Accept(state, state.With(yaw: PlanarMath.YawTowards(dx, dz)));
        }

        /// <summary>Starts (or refreshes) a conversation hold of <paramref name="milliseconds"/>.</summary>
        public static NpcTransition Converse(NpcSnapshot state, int milliseconds) =>
            NpcTransition.Accept(state, state.With(state: (int)NpcStateCode.Converse, timer: milliseconds < 0 ? 0 : milliseconds));

        /// <summary>Ends a conversation hold now and resumes the standing behaviour.</summary>
        public static NpcTransition EndConverse(NpcSnapshot state, IReadOnlyList<PatrolPoint> route)
        {
            if (state.StateCode != NpcStateCode.Converse)
            {
                return NpcTransition.Refuse(NpcRefusal.Unchanged, state);
            }

            return NpcTransition.Accept(state, Resume(state.With(timer: 0), route));
        }

        /// <summary>npc.setMood within [-100, 100].</summary>
        public static NpcTransition SetMood(NpcSnapshot state, int mood)
        {
            if (mood < MoodMin || mood > MoodMax)
            {
                return NpcTransition.Refuse(NpcRefusal.MoodOutOfRange, state);
            }

            if (state.Mood == mood)
            {
                return NpcTransition.Refuse(NpcRefusal.Unchanged, state);
            }

            return NpcTransition.Accept(state, state.With(mood: mood));
        }

        /// <summary>
        /// Advances an NPC by <paramref name="elapsedMilliseconds"/> of logical time: patrol stepping and waiting,
        /// approach movement, conversation hold countdown. Idle and custom NPCs do not move.
        /// </summary>
        public static NpcTransition Step(NpcSnapshot state, NpcProfile profile, IReadOnlyList<PatrolPoint> route, int elapsedMilliseconds)
        {
            int elapsed = elapsedMilliseconds < 0 ? 0 : elapsedMilliseconds;
            switch (state.StateCode)
            {
                case NpcStateCode.Converse:
                {
                    int timer = state.TimerMilliseconds - elapsed;
                    if (timer > 0)
                    {
                        return new NpcTransition(NpcRefusal.None, state.With(timer: timer), false, false, -1);
                    }

                    NpcSnapshot resumed = Resume(state.With(timer: 0), route);
                    return new NpcTransition(NpcRefusal.None, resumed, resumed.State != state.State, false, -1);
                }

                case NpcStateCode.Patrol:
                    return StepPatrol(state, profile, route, elapsed);

                case NpcStateCode.Approach:
                {
                    NpcSnapshot moved = MoveTo(state, profile, elapsed, out bool arrived);
                    if (!arrived)
                    {
                        return new NpcTransition(NpcRefusal.None, moved, false, false, -1);
                    }

                    NpcSnapshot resumed = Resume(moved, route);
                    return new NpcTransition(NpcRefusal.None, resumed, resumed.State != state.State, true, -1);
                }

                default:
                {
                    if (state.TimerMilliseconds > 0)
                    {
                        int timer = state.TimerMilliseconds - elapsed;
                        return new NpcTransition(NpcRefusal.None, state.With(timer: timer < 0 ? 0 : timer), false, false, -1);
                    }

                    return new NpcTransition(NpcRefusal.None, state, false, false, -1);
                }
            }
        }

        /// <summary>
        /// Applies a schedule phase: when <paramref name="phase"/> differs from npc.schedulePhase the standing behaviour
        /// becomes the phase's behaviour, and when the phase names a location the NPC walks there first.
        /// </summary>
        public static NpcTransition EnterPhase(NpcSnapshot state, int phase, SchedulePhase definition, IReadOnlyList<PatrolPoint> route)
        {
            if (state.SchedulePhase == phase)
            {
                return NpcTransition.Refuse(NpcRefusal.Unchanged, state);
            }

            int behaviour = IsStandingBehaviour(definition.Behaviour) ? definition.Behaviour : state.Behaviour;
            if (behaviour == (int)NpcStateCode.Patrol && (route == null || route.Count == 0))
            {
                behaviour = (int)NpcStateCode.Idle;
            }

            NpcSnapshot next = state.With(schedulePhase: phase, behaviour: behaviour);
            if (state.StateCode == NpcStateCode.Converse)
            {
                return NpcTransition.Accept(state, next);
            }

            if (definition.HasLocation)
            {
                return NpcTransition.Accept(state, next.With(state: (int)NpcStateCode.Approach, targetX: definition.X, targetZ: definition.Z, timer: 0));
            }

            return NpcTransition.Accept(state, Resume(next.With(timer: 0), route));
        }

        /// <summary>
        /// True when an NPC is processed in step <paramref name="step"/>: every step while its region is resident, every
        /// <paramref name="unloadedStride"/>-th step (with that many steps of elapsed time) while it is not.
        /// </summary>
        public static bool ShouldUpdate(ulong step, bool resident, int unloadedStride)
        {
            if (resident || unloadedStride <= 1)
            {
                return true;
            }

            return step % (ulong)unloadedStride == 0UL;
        }

        /// <summary>Elapsed logical time of one update.</summary>
        public static int ElapsedOf(int stepMilliseconds, bool resident, int unloadedStride) =>
            resident || unloadedStride <= 1 ? stepMilliseconds : stepMilliseconds * unloadedStride;

        public static int ClampMood(int mood) => mood < MoodMin ? MoodMin : (mood > MoodMax ? MoodMax : mood);

        private static NpcTransition StepPatrol(NpcSnapshot state, NpcProfile profile, IReadOnlyList<PatrolPoint> route, int elapsed)
        {
            if (route == null || route.Count == 0)
            {
                NpcSnapshot idle = state.With(state: (int)NpcStateCode.Idle, behaviour: (int)NpcStateCode.Idle, timer: 0);
                return new NpcTransition(NpcRefusal.None, idle, true, false, -1);
            }

            int index = state.PatrolIndex;
            if (index < 0 || index >= route.Count)
            {
                index = 0;
            }

            if (state.TimerMilliseconds > 0)
            {
                int timer = state.TimerMilliseconds - elapsed;
                if (timer > 0)
                {
                    return new NpcTransition(NpcRefusal.None, state.With(timer: timer), false, false, -1);
                }

                int next = (index + 1) % route.Count;
                return new NpcTransition(
                    NpcRefusal.None,
                    state.With(patrolIndex: next, targetX: route[next].X, targetZ: route[next].Z, timer: 0),
                    false,
                    false,
                    -1);
            }

            NpcSnapshot aimed = state.With(patrolIndex: index, targetX: route[index].X, targetZ: route[index].Z);
            NpcSnapshot moved = MoveTo(aimed, profile, elapsed, out bool arrived);
            if (!arrived)
            {
                return new NpcTransition(NpcRefusal.None, moved, false, false, -1);
            }

            int wait = profile.WaitMilliseconds;
            if (wait <= 0)
            {
                int next = (index + 1) % route.Count;
                moved = moved.With(patrolIndex: next, targetX: route[next].X, targetZ: route[next].Z);
            }
            else
            {
                moved = moved.With(timer: wait);
            }

            return new NpcTransition(NpcRefusal.None, moved, false, true, index);
        }

        private static NpcSnapshot MoveTo(NpcSnapshot state, NpcProfile profile, int elapsed, out bool arrived)
        {
            int distance = PlanarMath.Distance(state.PosX, state.PosZ, state.TargetX, state.TargetZ);
            if (distance <= profile.ArriveRadiusMillimetres)
            {
                arrived = true;
                return state.With(posX: state.TargetX, posZ: state.TargetZ);
            }

            int step = PlanarMath.Travel(profile.SpeedMillimetresPerSecond, elapsed);
            arrived = PlanarMath.MoveToward(state.PosX, state.PosZ, state.TargetX, state.TargetZ, step, out int x, out int z);
            int yaw = state.Yaw;
            int dx = PlanarMath.Sub(state.TargetX, state.PosX);
            int dz = PlanarMath.Sub(state.TargetZ, state.PosZ);
            if (dx != 0 || dz != 0)
            {
                yaw = PlanarMath.YawTowards(dx, dz);
            }

            return state.With(posX: x, posZ: z, yaw: yaw);
        }

        // The standing behaviour as a state: a patrol re-aims at its current point.
        private static NpcSnapshot Resume(NpcSnapshot state, IReadOnlyList<PatrolPoint> route)
        {
            int behaviour = state.Behaviour;
            if (behaviour == (int)NpcStateCode.Patrol)
            {
                if (route == null || route.Count == 0)
                {
                    return state.With(state: (int)NpcStateCode.Idle, behaviour: (int)NpcStateCode.Idle);
                }

                int index = state.PatrolIndex < 0 || state.PatrolIndex >= route.Count ? 0 : state.PatrolIndex;
                return state.With(state: behaviour, patrolIndex: index, targetX: route[index].X, targetZ: route[index].Z);
            }

            return state.With(state: IsStandingBehaviour(behaviour) ? behaviour : (int)NpcStateCode.Idle);
        }
    }

    /// <summary>One phase of an NPC's day.</summary>
    public readonly struct SchedulePhase
    {
        public SchedulePhase(int startMilliseconds, int behaviour, bool hasLocation, int x, int z)
        {
            StartMilliseconds = startMilliseconds;
            Behaviour = behaviour;
            HasLocation = hasLocation;
            X = x;
            Z = z;
        }

        /// <summary>When the phase starts, in milliseconds after the day's start.</summary>
        public int StartMilliseconds { get; }

        /// <summary>The standing behaviour of the phase (an <see cref="NpcStateCode"/> value).</summary>
        public int Behaviour { get; }

        public bool HasLocation { get; }

        public int X { get; }

        public int Z { get; }
    }

    /// <summary>Schedule phase resolution by world time.</summary>
    public static class ScheduleRules
    {
        /// <summary>
        /// World time in milliseconds of a logical step: steps x step duration + the schedule's start offset.
        /// </summary>
        public static long WorldTime(ulong step, int stepMilliseconds, int startOffsetMilliseconds)
        {
            const ulong Cap = 1UL << 40;
            long steps = (long)(step > Cap ? Cap : step);
            long duration = stepMilliseconds < 1 ? 1L : stepMilliseconds;
            return steps * duration + startOffsetMilliseconds;
        }

        /// <summary>
        /// The phase index at <paramref name="worldTimeMilliseconds"/>: the last phase whose start is not after the time
        /// of day; before the first start, the last phase (it wraps over midnight). -1 when there are no phases.
        /// Phases must be sorted by start.
        /// </summary>
        public static int PhaseAt(long worldTimeMilliseconds, int dayLengthMilliseconds, IReadOnlyList<SchedulePhase> phases)
        {
            if (phases == null || phases.Count == 0)
            {
                return -1;
            }

            long day = dayLengthMilliseconds < 1 ? 1 : dayLengthMilliseconds;
            long timeOfDay = worldTimeMilliseconds % day;
            if (timeOfDay < 0)
            {
                timeOfDay += day;
            }

            int found = phases.Count - 1;
            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i].StartMilliseconds <= timeOfDay)
                {
                    found = i;
                }
            }

            return found;
        }

        /// <summary>True when phases are sorted by start and every start lies within the day.</summary>
        public static bool IsWellFormed(int dayLengthMilliseconds, IReadOnlyList<SchedulePhase> phases)
        {
            if (dayLengthMilliseconds < 1 || phases == null)
            {
                return false;
            }

            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i].StartMilliseconds < 0 || phases[i].StartMilliseconds >= dayLengthMilliseconds)
                {
                    return false;
                }

                if (i > 0 && phases[i].StartMilliseconds <= phases[i - 1].StartMilliseconds)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
