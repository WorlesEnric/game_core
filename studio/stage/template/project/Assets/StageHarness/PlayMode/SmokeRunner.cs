#nullable enable
// GameCore Studio staging harness, PlayMode (P2.4): the mechanism's declared smoke test, run twice.
//
// Each run calls the proposal's smoke entry (StageHarness.json smokeType/smokeMethod, a public static method without
// parameters) which boots a tiny world with the mechanism's plugin mounted and returns a session with
// `GameApplicationRoot Root`, `void Step(int frame)`, `string SlotHash()` and `Dispose()`. The runner yields one frame
// at a time for `smokeSteps` frames, calling Step(frame) after each, and asserts the one-pump rule (exactly one
// sanctioned pump per frame, no duplicate or bypass pump) and that nothing logged an exception or an error. Each run
// writes <out>/smoke-<n>.json with the canonical slot hash; the stage runner compares the two (determinism step).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using GameCore.Unity.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Stage.Harness
{
    public sealed class SmokeRunner
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SmokeRun1() => Run(1);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SmokeRun2() => Run(2);

        private static IEnumerator Run(int run)
        {
            HarnessConfig config = HarnessFiles.Load();
            string name = "smoke-" + run + ".json";
            if (string.IsNullOrEmpty(config.smokeType))
            {
                HarnessFiles.Write(config, name, new HarnessJson().Num("run", run).Str("status", "skipped").Str("detail", "the proposal declares no smoke entry").ToString());
                Assert.Ignore("the proposal declares no smoke entry");
            }

            Type? type = HarnessFiles.FindType(config.smokeType);
            Assert.That(type, Is.Not.Null, "the smoke entry type " + config.smokeType + " is not compiled");
            MethodInfo? begin = type!.GetMethod(config.smokeMethod, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            Assert.That(begin, Is.Not.Null, config.smokeType + "." + config.smokeMethod + "() does not exist");

            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, kind) =>
            {
                if (kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert)
                {
                    errors.Add(kind + ": " + message);
                }
            };
            Application.logMessageReceived += capture;
            var clock = Stopwatch.StartNew();
            object? session = null;
            int frames = 0;
            int pumps = 0;
            int duplicate = 0;
            int bypass = 0;
            string lastViolation = string.Empty;
            string slotHash = string.Empty;
            int steps = Math.Max(1, config.smokeSteps);
            try
            {
                session = Invoke(begin!, null, Array.Empty<object>());
                Assert.That(session, Is.Not.Null, "the smoke entry returned no session");
                Type sessionType = session!.GetType();
                GameApplicationRoot? root = sessionType.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance)?.GetValue(session) as GameApplicationRoot;
                Assert.That(root, Is.Not.Null, "the smoke session exposes no GameApplicationRoot Root");
                MethodInfo? step = sessionType.GetMethod("Step", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                MethodInfo? hash = sessionType.GetMethod("SlotHash", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                Assert.That(step, Is.Not.Null, "the smoke session has no Step(int)");
                Assert.That(hash, Is.Not.Null, "the smoke session has no SlotHash()");

                int startFrame = Time.frameCount;
                int startPumps = root!.PumpCounter.SanctionedPumps;
                int startDuplicate = root.PumpCounter.DuplicateFramePumps;
                int startBypass = root.PumpCounter.BypassPumps;
                for (int frame = 1; frame <= steps; frame++)
                {
                    yield return null;
                    Invoke(step!, session, new object[] { frame });
                }

                frames = Time.frameCount - startFrame;
                pumps = root.PumpCounter.SanctionedPumps - startPumps;
                duplicate = root.PumpCounter.DuplicateFramePumps - startDuplicate;
                bypass = root.PumpCounter.BypassPumps - startBypass;
                lastViolation = root.PumpCounter.LastViolation;
                slotHash = Invoke(hash!, session, Array.Empty<object>()) as string ?? string.Empty;
            }
            finally
            {
                if (session is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else
                {
                    session?.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(session, null);
                }

                Application.logMessageReceived -= capture;
            }

            var report = new HarnessJson()
                .Num("run", run)
                .Str("status", "ran")
                .Num("steps", steps)
                .Num("frames", frames)
                .Num("sanctionedPumps", pumps)
                .Num("duplicateFramePumps", duplicate)
                .Num("bypassPumps", bypass)
                .Str("lastViolation", lastViolation.Length == 0 ? null : lastViolation)
                .Num("errors", errors.Count)
                .Str("firstError", errors.Count == 0 ? null : errors[0])
                .Str("slotHash", slotHash)
                .Num("milliseconds", clock.ElapsedMilliseconds);
            HarnessFiles.Write(config, name, report.ToString());
            UnityEngine.Debug.Log("[stage-harness] smoke " + report);

            Assert.That(errors, Is.Empty, "the smoke run logged errors or exceptions");
            Assert.That(pumps, Is.EqualTo(frames).Within(1), "exactly one sanctioned pump per frame");
            Assert.That(duplicate + bypass, Is.EqualTo(0), "no duplicate or bypass pump: " + lastViolation);
            Assert.That(slotHash, Has.Length.EqualTo(64), "the smoke session reports a canonical slot hash");
        }

        private static object? Invoke(MethodInfo method, object? target, object[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw new InvalidOperationException(method.DeclaringType?.Name + "." + method.Name + " threw " + error.InnerException.GetType().Name + ": " + error.InnerException.Message, error.InnerException);
            }
        }
    }
}
