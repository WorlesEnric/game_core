#nullable enable
using System;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Parsed probe command line. Custom arguments are passed after the standard Unity player arguments
    /// (<c>-batchmode -nographics -logFile ...</c>) and are read from <see cref="Environment.GetCommandLineArgs"/>.
    /// </summary>
    public readonly struct ProbeArguments
    {
        private const string ResultArgumentName = "-probeResult";
        private const string MissingRegistrationArgumentName = "-probeMissingRegistration";
        private const string WorldDispatchArgumentName = "-probeWorldDispatch";

        private ProbeArguments(bool missingRegistration, bool worldDispatch, string? resultPath)
        {
            MissingRegistration = missingRegistration;
            WorldDispatch = worldDispatch;
            ResultPath = resultPath;
        }

        /// <summary>Runs the negative mode that requires an explicit missing-registration result.</summary>
        public bool MissingRegistration { get; }

        /// <summary>
        /// Runs the GC-005 owned-world mode: guarded dispatch, fail-stop, idle command-driven steps, two independent
        /// worlds and the fixed-step debt clock, all inside the standalone player (TEST-011, TEST-018).
        /// </summary>
        public bool WorldDispatch { get; }

        /// <summary>Destination path of the structured JSON result.</summary>
        public string? ResultPath { get; }

        /// <summary>True when the process was launched as a probe rather than as a normal player run.</summary>
        public bool IsProbeInvocation => MissingRegistration || WorldDispatch || !string.IsNullOrEmpty(ResultPath);

        /// <summary>True when a result destination was supplied; without it the probe cannot record evidence.</summary>
        public bool HasResultPath => !string.IsNullOrEmpty(ResultPath);

        public static ProbeArguments Parse(string[] arguments)
        {
            bool missingRegistration = false;
            bool worldDispatch = false;
            string? resultPath = null;
            if (arguments != null)
            {
                for (int i = 0; i < arguments.Length; i++)
                {
                    string argument = arguments[i];
                    if (argument == MissingRegistrationArgumentName)
                    {
                        missingRegistration = true;
                    }
                    else if (argument == WorldDispatchArgumentName)
                    {
                        worldDispatch = true;
                    }
                    else if (argument == ResultArgumentName && i + 1 < arguments.Length)
                    {
                        resultPath = arguments[i + 1];
                    }
                }
            }

            return new ProbeArguments(missingRegistration, worldDispatch, resultPath);
        }
    }
}
