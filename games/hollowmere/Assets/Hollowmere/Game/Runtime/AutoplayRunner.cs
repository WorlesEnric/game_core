// Hollowmere - runs an autoplay script against the game (P3.1: the player smoke test and the recorded playthrough).
//
// The runner executes one command at a time from Update, after the host reports Ready. walk steers toward a world
// point each frame through IAutoplayHost.SetMove (the real locomotion moves the player) and fails on a timeout or when
// the player made less than 0.2 m of progress for 6 s; waituntil polls IAutoplayHost.Check each frame; ui, advance,
// choose and interact are instantaneous (a refusal is a warning, not a failure). A failure logs
// "[autoplay] FAILED line N: ..." and quits with exit code 3; quit quits with its code; -quitAfterFrames quits with 0.
// Every executed command is logged as "[autoplay] <line> <verb> ... frame=<n>".
#nullable enable
using System.Globalization;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>What an autoplay script drives (implemented by the game).</summary>
    public interface IAutoplayHost
    {
        /// <summary>The game is booted and accepts commands.</summary>
        bool Ready { get; }

        /// <summary>The player's committed position in metres.</summary>
        Vector3 PlayerPosition { get; }

        /// <summary>Moves the player along a world-space direction (zero stops).</summary>
        void SetMove(Vector3 worldDirection, bool run);

        void SetFacing(float yawDegrees);

        void PressInteract();

        bool Dispatch(string uiCommand, out string detail);

        bool Advance();

        bool Choose(int option);

        /// <summary>Evaluates a condition string (game-defined vocabulary).</summary>
        bool Check(string condition, out string detail);

        /// <summary>A frame-log marker.</summary>
        void Mark(string label);

        void Log(string line);
    }

    /// <summary>Executes an <see cref="AutoplayScript"/> one command at a time.</summary>
    [DisallowMultipleComponent]
    public sealed class AutoplayRunner : MonoBehaviour
    {
        public const float ArriveDistance = 1.0f;
        public const float StuckDistance = 0.2f;
        public const float StuckSeconds = 6f;
        public const int FailureExitCode = 3;

        private AutoplayScript? script;
        private IAutoplayHost? host;
        private int index;
        private bool started;
        private int startFrame;
        private int quitAfterFrames;
        private float commandStart;
        private int commandStartFrame;
        private Vector3 progressAnchor;
        private float progressSince;
        private string lastCheck = string.Empty;

        public bool Running => script != null && !Finished && !Failed;

        public bool Finished { get; private set; }

        public bool Failed { get; private set; }

        public string FailureText { get; private set; } = string.Empty;

        /// <summary>The source line of the command being executed (0 before the first).</summary>
        public int CurrentLine { get; private set; }

        public int Executed { get; private set; }

        /// <summary>Starts <paramref name="autoplay"/>; quits with 0 after <paramref name="quitAfter"/> frames when positive.</summary>
        public void Run(AutoplayScript autoplay, IAutoplayHost game, int quitAfter)
        {
            script = autoplay ?? throw new System.ArgumentNullException(nameof(autoplay));
            host = game ?? throw new System.ArgumentNullException(nameof(game));
            quitAfterFrames = quitAfter;
            index = 0;
            started = false;
            Finished = false;
            Failed = false;
            FailureText = string.Empty;
            CurrentLine = 0;
            Executed = 0;
            startFrame = Time.frameCount;
            host.Log("[autoplay] script with " + autoplay.Commands.Count.ToString(CultureInfo.InvariantCulture) + " commands, quitAfterFrames="
                + quitAfter.ToString(CultureInfo.InvariantCulture) + " frame=" + Time.frameCount.ToString(CultureInfo.InvariantCulture));
        }

        private void Update()
        {
            if (script == null || host == null)
            {
                return;
            }

            if (quitAfterFrames > 0 && Time.frameCount - startFrame >= quitAfterFrames)
            {
                host.SetMove(Vector3.zero, false);
                host.Log("[autoplay] quitAfterFrames " + quitAfterFrames.ToString(CultureInfo.InvariantCulture) + " reached at frame "
                    + Time.frameCount.ToString(CultureInfo.InvariantCulture) + " (executed " + Executed.ToString(CultureInfo.InvariantCulture) + " commands)");
                Quit(0);
                return;
            }

            if (Finished || Failed || !host.Ready)
            {
                return;
            }

            // Several instantaneous commands may run in one frame; a waiting command returns.
            for (int guard = 0; guard < 64 && index < script.Commands.Count; guard++)
            {
                AutoplayCommand command = script.Commands[index];
                if (!started)
                {
                    Begin(command);
                }

                if (!Step(command))
                {
                    return;
                }

                if (Failed || Finished)
                {
                    return;
                }

                index++;
                started = false;
            }

            if (index >= script.Commands.Count && !Finished)
            {
                Finished = true;
                host.SetMove(Vector3.zero, false);
                host.Log("[autoplay] finished " + Executed.ToString(CultureInfo.InvariantCulture) + " commands at frame " + Time.frameCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void Begin(AutoplayCommand command)
        {
            started = true;
            CurrentLine = command.Line;
            commandStart = Time.realtimeSinceStartup;
            commandStartFrame = Time.frameCount;
            progressAnchor = host!.PlayerPosition;
            progressSince = commandStart;
            lastCheck = string.Empty;
            Executed++;
            host.Log("[autoplay] " + command.Line.ToString(CultureInfo.InvariantCulture) + " " + command.Text + " frame=" + Time.frameCount.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Runs one frame of <paramref name="command"/>; true when it completed.</summary>
        private bool Step(AutoplayCommand command)
        {
            IAutoplayHost game = host!;
            float elapsed = Time.realtimeSinceStartup - commandStart;
            switch (command.Verb)
            {
                case AutoplayVerb.Wait:
                    return elapsed >= command.Seconds;
                case AutoplayVerb.WaitFrames:
                    return Time.frameCount - commandStartFrame >= command.Number;
                case AutoplayVerb.Ui:
                    if (!game.Dispatch(command.Argument, out string detail))
                    {
                        game.Log("[autoplay] warning line " + command.Line.ToString(CultureInfo.InvariantCulture) + ": ui " + command.Argument + " refused: " + detail);
                    }

                    return true;
                case AutoplayVerb.Walk:
                    return Walk(command, elapsed);
                case AutoplayVerb.Face:
                    game.SetFacing(command.Seconds);
                    return true;
                case AutoplayVerb.Interact:
                    game.PressInteract();
                    return true;
                case AutoplayVerb.Advance:
                    if (!game.Advance())
                    {
                        game.Log("[autoplay] warning line " + command.Line.ToString(CultureInfo.InvariantCulture) + ": advance refused");
                    }

                    return true;
                case AutoplayVerb.Choose:
                    if (!game.Choose(command.Number))
                    {
                        game.Log("[autoplay] warning line " + command.Line.ToString(CultureInfo.InvariantCulture) + ": choose " + command.Number.ToString(CultureInfo.InvariantCulture) + " refused");
                    }

                    return true;
                case AutoplayVerb.WaitUntil:
                    if (game.Check(command.Argument, out string state))
                    {
                        game.Log("[autoplay] " + command.Line.ToString(CultureInfo.InvariantCulture) + " reached '" + command.Argument + "' after "
                            + elapsed.ToString("F2", CultureInfo.InvariantCulture) + " s (" + state + ")");
                        return true;
                    }

                    lastCheck = state;
                    if (elapsed > Timeout(command))
                    {
                        Fail(command, "waituntil '" + command.Argument + "' timed out after " + Timeout(command).ToString("F0", CultureInfo.InvariantCulture) + " s (last: " + lastCheck + ")");
                    }

                    return false;
                case AutoplayVerb.Mark:
                    game.Mark(command.Argument);
                    return true;
                case AutoplayVerb.Log:
                    game.Log("[autoplay] " + command.Argument);
                    return true;
                case AutoplayVerb.Quit:
                    game.SetMove(Vector3.zero, false);
                    game.Log("[autoplay] quit " + command.Number.ToString(CultureInfo.InvariantCulture) + " at frame " + Time.frameCount.ToString(CultureInfo.InvariantCulture));
                    Finished = true;
                    Quit(command.Number);
                    return true;
                default:
                    Fail(command, "unsupported verb " + command.Verb);
                    return false;
            }
        }

        private bool Walk(AutoplayCommand command, float elapsed)
        {
            IAutoplayHost game = host!;
            Vector3 position = game.PlayerPosition;
            Vector3 delta = new Vector3(command.X - position.x, 0f, command.Z - position.z);
            float distance = delta.magnitude;
            if (distance <= ArriveDistance)
            {
                game.SetMove(Vector3.zero, false);
                game.Log("[autoplay] " + command.Line.ToString(CultureInfo.InvariantCulture) + " arrived at (" + F(position.x) + ", " + F(position.z) + ") after "
                    + elapsed.ToString("F2", CultureInfo.InvariantCulture) + " s");
                return true;
            }

            float now = Time.realtimeSinceStartup;
            Vector3 moved = position - progressAnchor;
            moved.y = 0f;
            if (moved.magnitude >= StuckDistance)
            {
                progressAnchor = position;
                progressSince = now;
            }
            else if (now - progressSince > StuckSeconds)
            {
                game.SetMove(Vector3.zero, false);
                Fail(command, "walk stuck at (" + F(position.x) + ", " + F(position.z) + "), " + F(distance) + " m from (" + F(command.X) + ", " + F(command.Z)
                    + "): less than " + F(StuckDistance) + " m in " + F(StuckSeconds) + " s");
                return false;
            }

            if (elapsed > Timeout(command))
            {
                game.SetMove(Vector3.zero, false);
                Fail(command, "walk timed out after " + Timeout(command).ToString("F0", CultureInfo.InvariantCulture) + " s at (" + F(position.x) + ", " + F(position.z) + "), "
                    + F(distance) + " m from the target");
                return false;
            }

            game.SetMove(delta / distance, command.Run);
            return false;
        }

        private static float Timeout(AutoplayCommand command) => command.Seconds > 0f ? command.Seconds : AutoplayScript.DefaultTimeoutSeconds;

        private void Fail(AutoplayCommand command, string reason)
        {
            Failed = true;
            FailureText = "line " + command.Line.ToString(CultureInfo.InvariantCulture) + ": " + reason;
            host?.Log("[autoplay] FAILED " + FailureText);
            Quit(FailureExitCode);
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            Debug.Log("[autoplay] Application.Quit(" + code.ToString(CultureInfo.InvariantCulture) + ") is ignored in the Editor");
#else
            Application.Quit(code);
#endif
        }

        private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    }
}
