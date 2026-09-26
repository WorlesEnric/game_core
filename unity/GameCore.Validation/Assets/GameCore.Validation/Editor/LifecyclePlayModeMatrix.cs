#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace GameCore.Validation.Editor
{
    /// <summary>
    /// GC-022 Play Mode reload matrix: the 2x2 of {domain reload, scene reload} settings, every combination in its
    /// own Editor process invocation and ten real enter/exit cycles per combination. It generalises
    /// <see cref="PlaySessionSmoke"/> (the GC-005 twenty-cycle evidence) from one reload setting to the full matrix
    /// and adds the per-session reset accounting that <c>GameCoreApplicationReset</c> exposes.
    ///
    /// The combination is selected by the <c>-gc022Combination</c> command line argument (the environment variable
    /// <c>GC022_COMBINATION</c> is an accepted fallback) so that an intermittent Editor hang in one combination
    /// cannot lose the other three. Each cycle's evidence is appended to
    /// <c>&lt;output&gt;/&lt;combination&gt;.jsonl</c> at the moment the cycle becomes observable, so an external
    /// watchdog that kills the process still leaves the combination and the cycle index it died at on disk. A
    /// combination is <c>Pass</c> only when all requested cycles completed with every assertion holding; the process
    /// exits 0 on success and 1 on an assertion failure, and the runner maps its watchdog's kill to its own code.
    ///
    /// The Editor's own enter-play settings are captured before anything is changed and restored by
    /// <see cref="Finish"/>, so the qualification project is left exactly as it was found.
    /// </summary>
    [InitializeOnLoad]
    public static class LifecyclePlayModeMatrix
    {
        /// <summary>Namespace of every SessionState key; SessionState survives a domain reload and is not reset by it.</summary>
        private const string Prefix = "GameCore.LifecyclePlayModeMatrix.";

        /// <summary>Routed host frames every cycle must observe before it is closed (the pump runs once per frame).</summary>
        private const int MinimumRoutedFrames = 20;

        /// <summary>
        /// Update-callback ceiling of one cycle. It is a watchdog of its own: a cycle that never routes
        /// <see cref="MinimumRoutedFrames"/> frames fails instead of spinning until the external timeout.
        /// </summary>
        private const int MaximumUpdateCallbacksPerCycle = 600;

        private const string CombinationArgument = "-gc022Combination";
        private const string CyclesArgument = "-gc022Cycles";
        private const string OutputArgument = "-gc022OutputDirectory";
        private const string CombinationVariable = "GC022_COMBINATION";
        private const string CyclesVariable = "GC022_CYCLES";
        private const string OutputVariable = "GC022_OUTPUT_DIRECTORY";

        /// <summary>The frozen combinations of the matrix, in the order the runner documents them.</summary>
        private static readonly Combination[] Combinations =
        {
            new Combination("reload-on-scene-on", false, EnterPlayModeOptions.None),
            new Combination("reload-on-scene-off", true, EnterPlayModeOptions.DisableSceneReload),
            new Combination("reload-off-scene-on", true, EnterPlayModeOptions.DisableDomainReload),
            new Combination(
                "reload-off-scene-off",
                true,
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload),
        };

        /// <summary>
        /// Domain generation of this loaded domain. The <c>[InitializeOnLoad]</c> constructor runs once per domain
        /// load, so a changed value is the observable proof that entering Play Mode reloaded the domain, which is
        /// what tells a session whether the static counters survived from the previous session.
        /// </summary>
        private static int domainGeneration;

        /// <summary>
        /// Host of the session being left. It is only meaningful when the domain survived Play Mode, i.e. when
        /// domain reload is disabled; across a domain reload the field is reset and the disposed-state checks are
        /// reported as not applicable instead of being silently skipped.
        /// </summary>
        private static UnityWorldHost? leavingHost;

        static LifecyclePlayModeMatrix()
        {
            int loads = SessionState.GetInt(Prefix + "domainLoads", 0) + 1;
            SessionState.SetInt(Prefix + "domainLoads", loads);
            domainGeneration = loads;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnUpdate;
        }

        /// <summary>
        /// Batchmode entry point, called through
        /// <c>-executeMethod GameCore.Validation.Editor.LifecyclePlayModeMatrix.Run</c>.
        /// </summary>
        public static void Run()
        {
            try
            {
                // Capture first: whatever happens afterwards, Finish restores the project's own enter-play settings.
                CaptureOriginalSettings();
                string[] arguments = Environment.GetCommandLineArgs();
                string combinationName = ResolveSetting(
                    arguments, CombinationArgument, CombinationVariable, string.Empty);
                if (combinationName.Length == 0)
                {
                    throw new InvalidOperationException(
                        "no combination was given; pass " + CombinationArgument + " <name> or set "
                        + CombinationVariable + ", one of " + KnownNames() + ".");
                }

                Combination combination = DescribeCombination(combinationName);
                int cycles = ParseCycles(ResolveSetting(arguments, CyclesArgument, CyclesVariable, "10"));
                string outputDirectory = Path.GetFullPath(
                    ResolveSetting(arguments, OutputArgument, OutputVariable, DefaultOutputDirectory()));

                PrepareSession(combination, cycles, outputDirectory);
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(JsonlPath(), string.Empty);
                string summaryPath = SummaryPath(combination.Name);
                if (File.Exists(summaryPath))
                {
                    File.Delete(summaryPath);
                }

                Debug.Log(
                    "[GC022] matrix started; combination=" + combination.Name
                    + "; cycles=" + cycles.ToString(CultureInfo.InvariantCulture)
                    + "; domainReload=" + combination.DomainReloadEnabled.ToString()
                    + "; sceneReload=" + combination.SceneReloadEnabled.ToString()
                    + "; optionsEnabled=" + combination.OptionsEnabled.ToString()
                    + "; options=" + combination.Options.ToString()
                    + "; output=" + outputDirectory
                    + "; jsonl=" + JsonlPath()
                    + "; batchMode=" + Application.isBatchMode.ToString());

                SessionState.SetBool(Prefix + "active", true);
                BeginCycle();
            }
            catch (Exception exception)
            {
                Debug.LogError("[GC022] the matrix could not start: " + exception);
                FailWith(new List<string> { exception.Message }, "startup");
            }
        }

        private static void CaptureOriginalSettings()
        {
            if (SessionState.GetBool(Prefix + "settingsCaptured", false))
            {
                // A previous invocation was killed before it could restore. The values it captured are the project's
                // own settings, so keep them instead of capturing a possibly mutated state.
                return;
            }

            SessionState.SetBool(Prefix + "originalEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Prefix + "originalOptions", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(Prefix + "settingsCaptured", true);
        }

        private static void PrepareSession(Combination combination, int cycles, string outputDirectory)
        {
            SessionState.SetBool(Prefix + "active", false);
            SessionState.SetBool(Prefix + "inCycle", false);
            SessionState.SetString(Prefix + "combination", combination.Name);
            SessionState.SetString(Prefix + "outputDirectory", outputDirectory);
            SessionState.SetInt(Prefix + "cycles", cycles);
            SessionState.SetInt(Prefix + "cycle", 0);
            SessionState.SetInt(Prefix + "attempted", 0);
            SessionState.SetInt(Prefix + "attemptedTotal", 0);
            SessionState.SetInt(Prefix + "frames", 0);
            SessionState.SetInt(Prefix + "pumpedFrames", 0);
            SessionState.SetInt(Prefix + "entryPumpCount", 0);
            SessionState.SetString(Prefix + "previousSession", string.Empty);
            SessionState.SetString(Prefix + "session", string.Empty);
            SessionState.SetString(Prefix + "inCycleFailures", string.Empty);
            SessionState.SetString(
                Prefix + "startUtcTicks",
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static void BeginCycle()
        {
            Combination combination = DescribeCombination(SessionState.GetString(Prefix + "combination", string.Empty));
            int completed = SessionState.GetInt(Prefix + "cycle", 0);
            int attempted = completed + 1;

            // The previous session's host reference never belongs to this session, and with domain reload disabled the
            // static field would otherwise survive into it.
            leavingHost = null;

            EditorSettings.enterPlayModeOptionsEnabled = combination.OptionsEnabled;
            EditorSettings.enterPlayModeOptions = combination.Options;

            SessionState.SetInt(Prefix + "prePlayDomainGeneration", domainGeneration);
            SessionState.SetInt(Prefix + "prePlayRunCount", GameCoreApplicationReset.RunCount);
            SessionState.SetInt(Prefix + "prePlayInstallCount", GameCorePlayerLoopInstaller.InstallCount);
            SessionState.SetInt(Prefix + "prePlayBootstrapCount", GameCoreApplicationBootstrap.BootstrapCount);
            SessionState.SetInt(Prefix + "attempted", attempted);
            SessionState.SetInt(Prefix + "attemptedTotal", attempted);
            SessionState.SetInt(Prefix + "frames", 0);
            SessionState.SetInt(Prefix + "pumpedFrames", 0);
            SessionState.SetBool(Prefix + "inCycle", false);
            SessionState.SetString(Prefix + "session", string.Empty);
            SessionState.SetString(Prefix + "inCycleFailures", string.Empty);

            // Written before the session starts, so a watchdog kill during this cycle leaves a last line that names
            // the combination and the cycle index the process died at.
            AppendJsonLine(
                "cycle-begin",
                combination,
                attempted,
                "Attempted",
                0,
                0,
                "EnteringPlayMode",
                new List<string>());

            Debug.Log(
                "[GC022] " + combination.Name + " cycle " + attempted.ToString(CultureInfo.InvariantCulture)
                + "/" + SessionState.GetInt(Prefix + "cycles", 0).ToString(CultureInfo.InvariantCulture)
                + " entering Play Mode");

            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Prefix + "active", false))
            {
                return;
            }

            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    EnterPlayMode();
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    LeavePlayMode();
                }
            }
            catch (Exception exception)
            {
                FailWith(
                    new List<string>
                    {
                        "cycle " + SessionState.GetInt(Prefix + "attempted", 0).ToString(CultureInfo.InvariantCulture)
                        + " raised: " + exception,
                    },
                    state.ToString());
            }
        }

        /// <summary>
        /// EnteredPlayMode assertions: one registry world, one application loop route, no bootstrap fallback, a
        /// running host, a fresh WorldId for the incarnation, and exactly one application reset for this session.
        /// </summary>
        private static void EnterPlayMode()
        {
            var failures = new List<string>();
            Combination combination = DescribeCombination(SessionState.GetString(Prefix + "combination", string.Empty));
            int attempted = SessionState.GetInt(Prefix + "attempted", 0);
            int registryCount = UnityWorldRegistry.Count;
            int installedNodes = GameCorePlayerLoopInstaller.CountInstalledNodes();

            bool reloadedOnEntry =
                domainGeneration != SessionState.GetInt(Prefix + "prePlayDomainGeneration", -1);
            int prePlayRunCount = SessionState.GetInt(Prefix + "prePlayRunCount", 0);
            int prePlayInstallCount = SessionState.GetInt(Prefix + "prePlayInstallCount", 0);
            int prePlayBootstrapCount = SessionState.GetInt(Prefix + "prePlayBootstrapCount", 0);

            Check(failures, registryCount == 1, "expected exactly one registry world, found " + Text(registryCount));
            Check(failures, installedNodes == 1, "expected exactly one application loop route, found " + Text(installedNodes));
            Check(
                failures,
                GameCoreApplicationBootstrap.FallbackCount == 0,
                "the bootstrap fell back to the infrastructure-only world "
                + Text(GameCoreApplicationBootstrap.FallbackCount) + " time(s)");
            Check(
                failures,
                GameCoreApplicationReset.RunCount >= 1,
                "the per-session reset did not run (RunCount=" + Text(GameCoreApplicationReset.RunCount) + ")");

            if (reloadedOnEntry)
            {
                // Entering Play Mode reloaded the domain, so these statics are fresh: exactly one of each is expected.
                Check(
                    failures,
                    GameCoreApplicationReset.RunCount == 1,
                    "a domain reload started this session, so exactly one reset was expected, found "
                    + Text(GameCoreApplicationReset.RunCount));
                Check(
                    failures,
                    GameCorePlayerLoopInstaller.InstallCount == 1,
                    "a domain reload started this session, so exactly one loop install was expected, found "
                    + Text(GameCorePlayerLoopInstaller.InstallCount));
                Check(
                    failures,
                    GameCoreApplicationBootstrap.BootstrapCount == 1,
                    "a domain reload started this session, so exactly one bootstrap was expected, found "
                    + Text(GameCoreApplicationBootstrap.BootstrapCount));
            }
            else
            {
                // The domain survived, so the counters are cumulative and must have advanced by exactly one session.
                Check(
                    failures,
                    GameCoreApplicationReset.RunCount == prePlayRunCount + 1,
                    "expected exactly one reset for this session: " + Text(prePlayRunCount) + " before, "
                    + Text(GameCoreApplicationReset.RunCount) + " now");
                Check(
                    failures,
                    GameCorePlayerLoopInstaller.InstallCount == prePlayInstallCount + 1,
                    "expected exactly one loop install for this session: " + Text(prePlayInstallCount)
                    + " before, " + Text(GameCorePlayerLoopInstaller.InstallCount) + " now");
                Check(
                    failures,
                    GameCoreApplicationBootstrap.BootstrapCount == prePlayBootstrapCount + 1,
                    "expected exactly one bootstrap for this session: " + Text(prePlayBootstrapCount)
                    + " before, " + Text(GameCoreApplicationBootstrap.BootstrapCount) + " now");
            }

            // The reset runs before the bootstrap installs the application route, so it must have found nothing to
            // clean: a surviving loop node or a surviving host from the previous session fails here.
            Check(
                failures,
                GameCoreApplicationReset.LastRemovedNodeCount == 0,
                "the session reset removed " + Text(GameCoreApplicationReset.LastRemovedNodeCount)
                + " leftover loop node(s); the previous session leaked its route");
            Check(
                failures,
                GameCoreApplicationReset.LastDisposedWorldCount == 0,
                "the session reset disposed " + Text(GameCoreApplicationReset.LastDisposedWorldCount)
                + " leftover host(s); the previous session leaked a world");

            string session = string.Empty;
            UnityWorldHost? host = registryCount == 1 ? UnityWorldRegistry.Hosts[0] : null;
            if (host == null)
            {
                failures.Add("no host is available for the per-session assertions");
            }
            else
            {
                session = host.World.Session.ToString();
                string previousSession = SessionState.GetString(Prefix + "previousSession", string.Empty);
                Check(
                    failures,
                    host.Lifecycle == WorldLifecycleState.Running,
                    "the bootstrap host is " + host.Lifecycle.ToString() + ", expected Running");
                Check(
                    failures,
                    !string.Equals(session, previousSession, StringComparison.Ordinal),
                    "the world incarnation " + session + " was reused; every session must own a fresh WorldId");
                Check(
                    failures,
                    host.ReentrantPumpCount == 0,
                    "the host refused " + Text(host.ReentrantPumpCount) + " reentrant pump(s)");
                Check(failures, host.FaultCount == 0, "the host recorded " + Text(host.FaultCount) + " fault(s)");

                SessionState.SetString(Prefix + "previousSession", session);
                SessionState.SetInt(Prefix + "entryPumpCount", host.PumpCount);
                leavingHost = host;
            }

            SessionState.SetString(Prefix + "session", session);
            SessionState.SetBool(Prefix + "reloadedOnEntry", reloadedOnEntry);
            SessionState.SetBool(Prefix + "inCycle", true);
            SessionState.SetInt(Prefix + "frames", 0);

            if (failures.Count != 0)
            {
                FailWith(failures, "EnteredPlayMode");
                return;
            }

            Debug.Log(
                "[GC022] " + combination.Name + " cycle " + Text(attempted) + " entered Play Mode; session=" + session
                + "; reloadedOnEntry=" + reloadedOnEntry.ToString()
                + "; runCount=" + Text(GameCoreApplicationReset.RunCount));
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(Prefix + "active", false)
                || !SessionState.GetBool(Prefix + "inCycle", false)
                || !EditorApplication.isPlaying
                || EditorApplication.isCompiling)
            {
                return;
            }

            int frames = SessionState.GetInt(Prefix + "frames", 0) + 1;
            SessionState.SetInt(Prefix + "frames", frames);

            UnityWorldHost? host = UnityWorldRegistry.Count == 1 ? UnityWorldRegistry.Hosts[0] : null;
            int entryPumpCount = SessionState.GetInt(Prefix + "entryPumpCount", 0);
            int routedFrames = host == null ? 0 : host.PumpCount - entryPumpCount;

            if (host != null && routedFrames >= MinimumRoutedFrames)
            {
                // The cycle has driven real routed frames; assert the live session and close it.
                SessionState.SetInt(Prefix + "pumpedFrames", routedFrames);
                SessionState.SetInt(Prefix + "frames", frames);
                SessionState.SetBool(Prefix + "inCycle", false);
                var failures = new List<string>();
                int installedNodes = GameCorePlayerLoopInstaller.CountInstalledNodes();
                Check(
                    failures,
                    installedNodes == 1,
                    "during the routed frames the application loop route count was " + Text(installedNodes)
                    + ", expected 1");
                SessionState.SetString(
                    Prefix + "inCycleFailures",
                    failures.Count == 0 ? string.Empty : string.Join("; ", failures));
                EditorApplication.isPlaying = false;
                return;
            }

            if (frames >= MaximumUpdateCallbacksPerCycle)
            {
                SessionState.SetInt(Prefix + "pumpedFrames", routedFrames);
                SessionState.SetBool(Prefix + "inCycle", false);
                var failures = new List<string>
                {
                    "the cycle drove " + Text(frames) + " update callback(s) but only " + Text(routedFrames)
                    + " routed host frame(s); expected at least " + Text(MinimumRoutedFrames),
                };
                SessionState.SetString(Prefix + "inCycleFailures", string.Join("; ", failures));
                EditorApplication.isPlaying = false;
            }
        }

        /// <summary>
        /// EnteredEditMode assertions: nothing survives the exit, and the retained host (only available when the
        /// domain survived) is disposed with no outstanding job and no retained resource.
        /// </summary>
        private static void LeavePlayMode()
        {
            var failures = new List<string>();
            Combination combination = DescribeCombination(SessionState.GetString(Prefix + "combination", string.Empty));
            int completed = SessionState.GetInt(Prefix + "cycle", 0);
            int attempted = SessionState.GetInt(Prefix + "attempted", completed + 1);
            int requested = SessionState.GetInt(Prefix + "cycles", 1);
            int pumpedFrames = SessionState.GetInt(Prefix + "pumpedFrames", 0);
            int frames = SessionState.GetInt(Prefix + "frames", 0);
            string inCycleFailures = SessionState.GetString(Prefix + "inCycleFailures", string.Empty);
            UnityWorldHost? host = leavingHost;

            Check(
                failures,
                UnityWorldRegistry.Count == 0,
                "the world registry still holds " + Text(UnityWorldRegistry.Count) + " host(s) after exit");
            Check(
                failures,
                GameCorePlayerLoopInstaller.CountInstalledNodes() == 0,
                "the application loop route survived the exit");

            if (host != null)
            {
                Check(
                    failures,
                    host.Lifecycle == WorldLifecycleState.Disposed,
                    "the retained host is " + host.Lifecycle.ToString() + ", expected Disposed");
                Check(failures, !host.IsEntityWorldCreated, "the retained host still owns its ECS storage");
                Check(
                    failures,
                    host.Ledger.OutstandingJobCount == 0,
                    "the retained host left " + Text(host.Ledger.OutstandingJobCount) + " job(s) outstanding");
                Check(
                    failures,
                    host.Ledger.RetainedResourceCount == 0,
                    "the retained host retained " + Text(host.Ledger.RetainedResourceCount) + " resource(s)");
            }

            if (inCycleFailures.Length != 0)
            {
                failures.Add(inCycleFailures);
            }

            leavingHost = null;
            bool passed = failures.Count == 0;
            AppendJsonLine(
                "cycle-result",
                combination,
                attempted,
                passed ? "Pass" : "Fail",
                frames,
                pumpedFrames,
                "EnteredEditMode",
                failures);

            if (!passed)
            {
                Debug.LogError("[GC022] " + combination.Name + " cycle " + Text(attempted) + " failed: "
                    + string.Join("; ", failures));
                WriteSummary(combination, completed, 0, "Fail", 1, failures);
                Finish(1);
                return;
            }

            completed++;
            SessionState.SetInt(Prefix + "cycle", completed);
            Debug.Log(
                "[GC022] " + combination.Name + " cycle " + Text(completed) + "/" + Text(requested)
                + " left Play Mode clean; routedFrames=" + Text(pumpedFrames));

            if (completed >= requested)
            {
                WriteSummary(combination, completed, 1, "Pass", 0, failures);
                Finish(0);
                return;
            }

            EditorApplication.delayCall += BeginCycle;
        }

        private static void FailWith(List<string> failures, string stage)
        {
            Debug.LogError("[GC022] " + stage + " failure: " + string.Join("; ", failures));
            try
            {
                RecordFailure(failures, stage, SessionState.GetInt(Prefix + "attempted", 0), 0);
                Combination combination = DescribeOrPlaceholder(
                    SessionState.GetString(Prefix + "combination", string.Empty));
                WriteSummary(
                    combination,
                    SessionState.GetInt(Prefix + "cycle", 0),
                    0,
                    "Fail",
                    1,
                    failures);
            }
            catch (Exception recording)
            {
                Debug.LogError("[GC022] could not record the failure: " + recording);
            }

            Finish(1);
        }

        private static void RecordFailure(List<string> failures, string stage, int attempted, int pumpedFrames)
        {
            Combination combination = DescribeOrPlaceholder(
                SessionState.GetString(Prefix + "combination", string.Empty));
            AppendJsonLine("abort", combination, attempted, "Aborted", SessionState.GetInt(Prefix + "frames", 0), pumpedFrames, stage, failures);
        }

        /// <summary>
        /// Writes the one structured summary of this invocation. <paramref name="combinationsCompleted"/> counts the
        /// combinations this process ran (always one), so the runner can aggregate without inventing a verdict.
        /// </summary>
        private static void WriteSummary(
            Combination combination,
            int completed,
            int combinationsCompleted,
            string result,
            int exitCode,
            List<string> failures)
        {
            double seconds = ElapsedMilliseconds() / 1000.0;
            string line = "{"
                + TextField("kind", "summary") + ", "
                + TextField("combination", combination.Name) + ", "
                + TextField("result", result) + ", "
                + NumberField("cyclesAttempted", SessionState.GetInt(Prefix + "attemptedTotal", 0)) + ", "
                + NumberField("cyclesCompleted", completed) + ", "
                + NumberField("cyclesRequested", SessionState.GetInt(Prefix + "cycles", 0)) + ", "
                + NumberField("combinationsCompleted", combinationsCompleted) + ", "
                + SecondsField("wallClockSecondsPerCombination", seconds) + ", "
                + BoolField("domainReload", combination.DomainReloadEnabled) + ", "
                + BoolField("sceneReload", combination.SceneReloadEnabled) + ", "
                + NumberField("exitCode", exitCode) + ", "
                + NumberField("failureCount", failures.Count) + ", "
                + TextField("detail", failures.Count == 0 ? string.Empty : string.Join("; ", failures))
                + "}\n";

            Directory.CreateDirectory(OutputDirectory());
            File.WriteAllText(SummaryPath(combination.Name), line);
            Debug.Log("[GC022] summary written to " + SummaryPath(combination.Name) + "; result=" + result);
        }

        /// <summary>
        /// Restores the captured enter-play settings and exits. This is the only exit path, so no run leaves the
        /// project's Editor settings changed.
        /// </summary>
        private static void Finish(int code)
        {
            SessionState.SetBool(Prefix + "active", false);
            if (SessionState.GetBool(Prefix + "settingsCaptured", false))
            {
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Prefix + "originalEnabled", false);
                EditorSettings.enterPlayModeOptions =
                    (EnterPlayModeOptions)SessionState.GetInt(Prefix + "originalOptions", 0);
                SessionState.SetBool(Prefix + "settingsCaptured", false);
                Debug.Log(
                    "[GC022] restored enter-play settings; optionsEnabled="
                    + EditorSettings.enterPlayModeOptionsEnabled.ToString()
                    + "; options=" + EditorSettings.enterPlayModeOptions.ToString());
            }

            Debug.Log("[GC022] exiting with code " + Text(code) + "; batchMode=" + Application.isBatchMode.ToString());
            EditorApplication.Exit(code);
        }

        private static void AppendJsonLine(
            string kind,
            Combination combination,
            int cycle,
            string status,
            int frames,
            int pumpedFrames,
            string stage,
            List<string> failures)
        {
            string line = "{"
                + TextField("kind", kind) + ", "
                + TextField("combination", combination.Name) + ", "
                + NumberField("cycle", cycle) + ", "
                + NumberField("cyclesRequested", SessionState.GetInt(Prefix + "cycles", 0)) + ", "
                + TextField("status", status) + ", "
                + NumberField("frames", frames) + ", "
                + NumberField("pumpedFrames", pumpedFrames) + ", "
                + BoolField("domainReload", combination.DomainReloadEnabled) + ", "
                + BoolField("sceneReload", combination.SceneReloadEnabled) + ", "
                + BoolField("optionsEnabled", combination.OptionsEnabled) + ", "
                + TextField("options", combination.Options.ToString()) + ", "
                + NumberField("domainGeneration", domainGeneration) + ", "
                + TextField("session", SessionState.GetString(Prefix + "session", string.Empty)) + ", "
                + TextField("stage", stage) + ", "
                + NumberField("failureCount", failures.Count) + ", "
                + NumberField("wallClockMs", ElapsedMilliseconds()) + ", "
                + TextField("detail", failures.Count == 0 ? string.Empty : string.Join("; ", failures))
                + "}\n";

            Directory.CreateDirectory(OutputDirectory());
            // AppendAllText opens, writes and closes the file, so every recorded line is flushed to the log file
            // before the process can be killed.
            File.AppendAllText(JsonlPath(), line);
        }

        private static void Check(List<string> failures, bool condition, string detail)
        {
            if (!condition)
            {
                failures.Add(detail);
            }
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string TextField(string name, string value)
        {
            return "\"" + name + "\": \"" + Escape(value) + "\"";
        }

        private static string NumberField(string name, int value)
        {
            return "\"" + name + "\": " + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string NumberField(string name, long value)
        {
            return "\"" + name + "\": " + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string SecondsField(string name, double value)
        {
            return "\"" + name + "\": " + value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string BoolField(string name, bool value)
        {
            return "\"" + name + "\": " + (value ? "true" : "false");
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ResolveSetting(
            string[] arguments,
            string argumentName,
            string variableName,
            string fallback)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], argumentName, StringComparison.Ordinal) && i + 1 < arguments.Length)
                {
                    return arguments[i + 1];
                }
            }

            string? fromEnvironment = Environment.GetEnvironmentVariable(variableName);
            return string.IsNullOrEmpty(fromEnvironment) ? fallback : fromEnvironment;
        }

        private static int ParseCycles(string value)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < 1)
            {
                throw new InvalidOperationException("the cycle count must be a positive integer, got '" + value + "'.");
            }

            return parsed;
        }

        private static string DefaultOutputDirectory()
        {
            string dataPath = Application.dataPath.TrimEnd('/', '\\');
            string? projectRoot = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return Path.GetFullPath("../../artifacts/gc-022/playmode-matrix");
            }

            return Path.GetFullPath(
                Path.Combine(projectRoot, "..", "..", "artifacts", "gc-022", "playmode-matrix"));
        }

        private static string OutputDirectory()
        {
            string stored = SessionState.GetString(Prefix + "outputDirectory", string.Empty);
            return stored.Length == 0 ? DefaultOutputDirectory() : stored;
        }

        private static string JsonlPath()
        {
            return Path.Combine(
                OutputDirectory(),
                SessionState.GetString(Prefix + "combination", "unknown") + ".jsonl");
        }

        private static string SummaryPath(string combinationName)
        {
            return Path.Combine(OutputDirectory(), combinationName + "-summary.json");
        }

        private static long ElapsedMilliseconds()
        {
            long startTicks;
            string stored = SessionState.GetString(Prefix + "startUtcTicks", string.Empty);
            if (!long.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out startTicks))
            {
                return 0L;
            }

            return (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
        }

        private static Combination DescribeCombination(string name)
        {
            Combination combination;
            if (!TryDescribeCombination(name, out combination))
            {
                throw new InvalidOperationException(
                    "unknown combination '" + name + "'; expected one of " + KnownNames() + ".");
            }

            return combination;
        }

        /// <summary>Recording path that never throws, so a startup failure is still named in its own evidence.</summary>
        private static Combination DescribeOrPlaceholder(string name)
        {
            Combination combination;
            if (TryDescribeCombination(name, out combination))
            {
                return combination;
            }

            return new Combination(name.Length == 0 ? "startup" : name, false, EnterPlayModeOptions.None);
        }

        private static bool TryDescribeCombination(string name, out Combination combination)
        {
            for (int i = 0; i < Combinations.Length; i++)
            {
                if (Combinations[i].Matches(name))
                {
                    combination = Combinations[i];
                    return true;
                }
            }

            combination = default;
            return false;
        }

        private static string KnownNames()
        {
            string names = string.Empty;
            for (int i = 0; i < Combinations.Length; i++)
            {
                names = i == 0 ? Combinations[i].Name : names + ", " + Combinations[i].Name;
            }

            return names;
        }

        /// <summary>One frozen reload combination of the matrix.</summary>
        private readonly struct Combination
        {
            public Combination(string name, bool optionsEnabled, EnterPlayModeOptions options)
            {
                Name = name;
                OptionsEnabled = optionsEnabled;
                Options = options;
            }

            public string Name { get; }

            /// <summary>True when <c>EditorSettings.enterPlayModeOptionsEnabled</c> is switched on.</summary>
            public bool OptionsEnabled { get; }

            /// <summary>The options written to <c>EditorSettings.enterPlayModeOptions</c>.</summary>
            public EnterPlayModeOptions Options { get; }

            /// <summary>True when this combination leaves Unity's domain reload enabled.</summary>
            public bool DomainReloadEnabled
            {
                get
                {
                    if (!OptionsEnabled)
                    {
                        return true;
                    }

                    return (Options & EnterPlayModeOptions.DisableDomainReload) == 0;
                }
            }

            /// <summary>True when this combination leaves Unity's scene reload enabled.</summary>
            public bool SceneReloadEnabled
            {
                get
                {
                    if (!OptionsEnabled)
                    {
                        return true;
                    }

                    return (Options & EnterPlayModeOptions.DisableSceneReload) == 0;
                }
            }

            public bool Matches(string name)
            {
                return string.Equals(Name, name, StringComparison.Ordinal);
            }
        }
    }
}
