// Hollowmere - the autoplay script (-autoplay <file>, P3.1).
//
// One command per line; '#' starts a comment (outside quotes); blank lines are ignored; verbs are case-insensitive;
// arguments are separated by spaces and may be double-quoted ("two words"). Grammar:
//
//   wait <seconds>                         real time
//   waitframes <n>                         rendered frames
//   ui <command>                           a UI command (newgame, resume, save.1, load.1, restart, open.journal ...)
//   walk <x> <z> [run] [timeout <s>]       steer the player to world (x, z) metres until within 1 m (default 60 s)
//   approach <entity> [within <m>] [run] [timeout <s>]
//                                          steer toward a placed entity's committed position, re-targeted every frame
//                                          (patrolling NPCs), until within <m> metres (default 1.6), then face it
//   face <yawDeg>                          turn the camera/player to a yaw in degrees
//   interact                               press Interact once
//   advance                                advance the dialogue
//   choose <i>                             choose dialogue option i
//   waituntil <condition...> [timeout <s>] poll a host condition each frame (default 60 s; a timeout fails the script)
//   mark <label...>                        a frame-log marker
//   log <text...>                          a log line
//   quit [code]                            quit the player (default 0)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hollowmere.Game
{
    /// <summary>The verbs of an autoplay script.</summary>
    public enum AutoplayVerb
    {
        Wait,
        WaitFrames,
        Ui,
        Walk,
        Approach,
        Face,
        Interact,
        Advance,
        Choose,
        WaitUntil,
        Mark,
        Log,
        Quit,
    }

    /// <summary>One parsed command (immutable).</summary>
    public sealed class AutoplayCommand
    {
        public AutoplayCommand(int line, AutoplayVerb verb, string text, string argument, float x, float z, bool run, float seconds, int number)
        {
            Line = line;
            Verb = verb;
            Text = text;
            Argument = argument;
            X = x;
            Z = z;
            Run = run;
            Seconds = seconds;
            Number = number;
        }

        /// <summary>1-based source line.</summary>
        public int Line { get; }

        public AutoplayVerb Verb { get; }

        /// <summary>The source line without its comment.</summary>
        public string Text { get; }

        /// <summary>ui command, waituntil condition, mark label or log text.</summary>
        public string Argument { get; }

        /// <summary>walk target x (metres).</summary>
        public float X { get; }

        /// <summary>walk target z (metres).</summary>
        public float Z { get; }

        /// <summary>walk: run instead of walking.</summary>
        public bool Run { get; }

        /// <summary>wait seconds, face yaw (degrees), walk/waituntil timeout (seconds; 0 = default).</summary>
        public float Seconds { get; }

        /// <summary>waitframes count, choose option, quit code.</summary>
        public int Number { get; }

        public override string ToString() => Text;
    }

    /// <summary>A parsed autoplay script (immutable).</summary>
    public sealed class AutoplayScript
    {
        public const float DefaultTimeoutSeconds = 60f;

        private AutoplayScript(IReadOnlyList<AutoplayCommand> commands)
        {
            Commands = commands;
        }

        public IReadOnlyList<AutoplayCommand> Commands { get; }

        /// <summary>Parses <paramref name="text"/>; on failure <paramref name="error"/> names the line ("line N: ...").</summary>
        public static bool TryParse(string text, out AutoplayScript? script, out string error)
        {
            script = null;
            error = string.Empty;
            var commands = new List<AutoplayCommand>();
            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int number = i + 1;
                if (!TryTokenize(lines[i], out List<string> tokens, out string stripped, out string problem))
                {
                    error = "line " + number.ToString(CultureInfo.InvariantCulture) + ": " + problem;
                    return false;
                }

                if (tokens.Count == 0)
                {
                    continue;
                }

                if (!TryCommand(number, stripped, tokens, out AutoplayCommand? command, out problem))
                {
                    error = "line " + number.ToString(CultureInfo.InvariantCulture) + ": " + problem;
                    return false;
                }

                commands.Add(command!);
            }

            if (commands.Count == 0)
            {
                error = "the script has no command";
                return false;
            }

            script = new AutoplayScript(commands);
            return true;
        }

        private static bool TryCommand(int line, string text, List<string> tokens, out AutoplayCommand? command, out string problem)
        {
            command = null;
            problem = string.Empty;
            string verb = tokens[0].ToLowerInvariant();
            int count = tokens.Count - 1;
            switch (verb)
            {
                case "wait":
                    if (count != 1 || !TryFloat(tokens[1], out float seconds) || seconds < 0f)
                    {
                        problem = "wait <seconds> (a non-negative number)";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.Wait, text, string.Empty, 0f, 0f, false, seconds, 0);
                    return true;
                case "waitframes":
                    if (count != 1 || !TryInt(tokens[1], out int frames) || frames < 0)
                    {
                        problem = "waitframes <n> (a non-negative integer)";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.WaitFrames, text, string.Empty, 0f, 0f, false, 0f, frames);
                    return true;
                case "ui":
                    if (count != 1)
                    {
                        problem = "ui <command> (one argument)";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.Ui, text, tokens[1], 0f, 0f, false, 0f, 0);
                    return true;
                case "walk":
                    return TryWalk(line, text, tokens, out command, out problem);
                case "approach":
                    return TryApproach(line, text, tokens, out command, out problem);
                case "face":
                    if (count != 1 || !TryFloat(tokens[1], out float yaw))
                    {
                        problem = "face <yawDeg>";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.Face, text, string.Empty, 0f, 0f, false, yaw, 0);
                    return true;
                case "interact":
                case "advance":
                    if (count != 0)
                    {
                        problem = verb + " takes no argument";
                        return false;
                    }

                    command = new AutoplayCommand(line, verb == "interact" ? AutoplayVerb.Interact : AutoplayVerb.Advance, text, string.Empty, 0f, 0f, false, 0f, 0);
                    return true;
                case "choose":
                    if (count != 1 || !TryInt(tokens[1], out int option) || option < 0)
                    {
                        problem = "choose <i> (a non-negative integer)";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.Choose, text, string.Empty, 0f, 0f, false, 0f, option);
                    return true;
                case "waituntil":
                    return TryWaitUntil(line, text, tokens, out command, out problem);
                case "mark":
                case "log":
                    if (count == 0)
                    {
                        problem = verb + " needs text";
                        return false;
                    }

                    command = new AutoplayCommand(line, verb == "mark" ? AutoplayVerb.Mark : AutoplayVerb.Log, text, string.Join(" ", tokens.GetRange(1, count)), 0f, 0f, false, 0f, 0);
                    return true;
                case "quit":
                    int code = 0;
                    if (count > 1 || (count == 1 && !TryInt(tokens[1], out code)))
                    {
                        problem = "quit [code]";
                        return false;
                    }

                    command = new AutoplayCommand(line, AutoplayVerb.Quit, text, string.Empty, 0f, 0f, false, 0f, code);
                    return true;
                default:
                    problem = "unknown command '" + tokens[0] + "'";
                    return false;
            }
        }

        /// <summary>approach &lt;entity name&gt; [within &lt;m&gt;] [run] [timeout &lt;s&gt;]: X carries the distance.</summary>
        private static bool TryApproach(int line, string text, List<string> tokens, out AutoplayCommand? command, out string problem)
        {
            command = null;
            problem = "approach <entity name> [within <m>] [run] [timeout <s>]";
            var name = new List<string>();
            float within = 1.6f;
            float timeout = 0f;
            bool run = false;
            for (int i = 1; i < tokens.Count; i++)
            {
                string token = tokens[i].ToLowerInvariant();
                if (token == "within" && i + 1 < tokens.Count && TryFloat(tokens[i + 1], out float metres) && metres > 0f)
                {
                    within = metres;
                    i++;
                }
                else if (token == "timeout" && i + 1 < tokens.Count && TryFloat(tokens[i + 1], out float seconds) && seconds > 0f)
                {
                    timeout = seconds;
                    i++;
                }
                else if (token == "run")
                {
                    run = true;
                }
                else
                {
                    name.Add(tokens[i]);
                }
            }

            if (name.Count == 0)
            {
                return false;
            }

            command = new AutoplayCommand(line, AutoplayVerb.Approach, text, string.Join(" ", name), within, 0f, run, timeout, 0);
            return true;
        }

        private static bool TryWalk(int line, string text, List<string> tokens, out AutoplayCommand? command, out string problem)
        {
            command = null;
            problem = "walk <x> <z> [run] [timeout <s>]";
            if (tokens.Count < 3 || !TryFloat(tokens[1], out float x) || !TryFloat(tokens[2], out float z))
            {
                return false;
            }

            bool run = false;
            float timeout = 0f;
            for (int i = 3; i < tokens.Count; i++)
            {
                string word = tokens[i].ToLowerInvariant();
                if (word == "run")
                {
                    run = true;
                }
                else if (word == "timeout" && i + 1 < tokens.Count && TryFloat(tokens[i + 1], out timeout) && timeout > 0f)
                {
                    i++;
                }
                else
                {
                    return false;
                }
            }

            command = new AutoplayCommand(line, AutoplayVerb.Walk, text, string.Empty, x, z, run, timeout, 0);
            problem = string.Empty;
            return true;
        }

        private static bool TryWaitUntil(int line, string text, List<string> tokens, out AutoplayCommand? command, out string problem)
        {
            command = null;
            problem = string.Empty;
            int end = tokens.Count;
            float timeout = 0f;
            if (tokens.Count >= 4 && string.Equals(tokens[tokens.Count - 2], "timeout", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryFloat(tokens[tokens.Count - 1], out timeout) || timeout <= 0f)
                {
                    problem = "waituntil ... timeout <s> (a positive number)";
                    return false;
                }

                end = tokens.Count - 2;
            }

            if (end <= 1)
            {
                problem = "waituntil <condition...> [timeout <s>]";
                return false;
            }

            command = new AutoplayCommand(line, AutoplayVerb.WaitUntil, text, string.Join(" ", tokens.GetRange(1, end - 1)), 0f, 0f, false, timeout, 0);
            return true;
        }

        /// <summary>Splits a line into tokens (double quotes group, '#' outside quotes starts a comment).</summary>
        private static bool TryTokenize(string line, out List<string> tokens, out string stripped, out string problem)
        {
            tokens = new List<string>();
            problem = string.Empty;
            var current = new StringBuilder();
            bool quoted = false;
            bool hasToken = false;
            int end = line.Length;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '#')
                {
                    end = i;
                    break;
                }

                if (c == '"')
                {
                    quoted = true;
                    hasToken = true;
                }
                else if (c == ' ' || c == '\t')
                {
                    if (hasToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }

            stripped = line.Substring(0, end).Trim();
            if (quoted)
            {
                problem = "unterminated quote";
                return false;
            }

            if (hasToken)
            {
                tokens.Add(current.ToString());
            }

            return true;
        }

        private static bool TryFloat(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool TryInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
