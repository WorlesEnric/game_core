#nullable enable
// GameCore.Studio.Edit - the Unity implementations of the admission seams (P2.4):
//   UnityAdmissionCompiler      AssetDatabase refresh + package re-resolve + script compilation; compile errors are
//                               reported in this domain, a clean compile ends in a domain reload (AdmissionResumer);
//   ReflectionAdmissionCatalog  the world's catalog fingerprint through GameCore.Gameplay.Compile.Entry.Verify (the
//                               in-memory re-bake of the project's WorldDefinition) and a mechanism catalog's
//                               fingerprint through its generated CatalogFingerprint / BuildCatalog() (Studio does not
//                               reference gameplay assemblies, so both are reached by name);
//   PythonAdmissionChecker      the repository checkers on the admitted package (studio/stage/slot-checks.py
//                               --package-dir: check_game_core_csharp.py + check_package_metadata.py rules).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GameCore.Contracts;
using UnityEditor;
using UnityEditor.Compilation;

namespace GameCore.Studio.Edit
{
    /// <summary>Recompiles the open project.</summary>
    public sealed class UnityAdmissionCompiler : IAdmissionCompiler
    {
        /// <summary>Seconds to wait for a compile to start before concluding nothing needed compiling.</summary>
        public double StartTimeoutSeconds { get; set; } = 90;

        public void Compile(string reason, Action<AdmissionCompileResult> done)
        {
            if (done == null)
            {
                throw new ArgumentNullException(nameof(done));
            }

            List<string> errors = new List<string>();
            bool started = false;
            bool finished = false;
            double deadline = EditorApplication.timeSinceStartup + StartTimeoutSeconds;
            Action<object>? onStarted = null;
            Action<string, CompilerMessage[]>? onAssembly = null;
            Action<object>? onFinished = null;
            EditorApplication.CallbackFunction? tick = null;

            void Unsubscribe()
            {
                CompilationPipeline.compilationStarted -= onStarted;
                CompilationPipeline.assemblyCompilationFinished -= onAssembly;
                CompilationPipeline.compilationFinished -= onFinished;
                EditorApplication.update -= tick;
            }

            onStarted = _ => started = true;
            onAssembly = (assembly, messages) =>
            {
                foreach (CompilerMessage message in messages)
                {
                    if (message.type == CompilerMessageType.Error)
                    {
                        errors.Add(message.message);
                    }
                }
            };
            onFinished = _ =>
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                Unsubscribe();
                done(errors.Count > 0
                    ? new AdmissionCompileResult(false, false, errors.Count + " compile error(s); first: " + errors[0], errors)
                    : new AdmissionCompileResult(true, true, "compiled; a domain reload follows"));
            };
            tick = () =>
            {
                if (finished)
                {
                    Unsubscribe();
                    return;
                }

                if (!started && !EditorApplication.isCompiling && EditorApplication.timeSinceStartup > deadline)
                {
                    finished = true;
                    Unsubscribe();
                    done(new AdmissionCompileResult(true, false, "nothing needed compiling"));
                }
            };
            CompilationPipeline.compilationStarted += onStarted;
            CompilationPipeline.assemblyCompilationFinished += onAssembly;
            CompilationPipeline.compilationFinished += onFinished;
            EditorApplication.update += tick;
            UnityEngine.Debug.Log("[GameCore Studio] stage: recompiling (" + reason + ")");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            UnityEditor.PackageManager.Client.Resolve();
            CompilationPipeline.RequestScriptCompilation();
        }
    }

    /// <summary>Reads catalog fingerprints by reflection.</summary>
    public sealed class ReflectionAdmissionCatalog : IAdmissionCatalog
    {
        public const string EntryType = "GameCore.Gameplay.Compile.Entry";

        public string? WorldFingerprint(out string? problem)
        {
            problem = null;
            Type? entry = FindType(EntryType);
            MethodInfo? verify = entry?.GetMethod("Verify", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (verify == null)
            {
                problem = "GameCore.Gameplay.Compile is not in the project";
                return null;
            }

            object? result;
            try
            {
                result = verify.Invoke(null, null);
            }
            catch (TargetInvocationException error)
            {
                problem = "Entry.Verify threw " + (error.InnerException ?? error).Message;
                return null;
            }

            string fingerprint = Read<string>(result, "CatalogFingerprint") ?? string.Empty;
            bool succeeded = Read<bool>(result, "Succeeded");
            string summary = Read<string>(result, "Summary") ?? string.Empty;
            if (fingerprint.Length != 64)
            {
                problem = "the world does not bake: " + summary + Problems(result);
                return null;
            }

            if (!succeeded)
            {
                UnityEngine.Debug.LogWarning("[GameCore Studio] stage: the committed bake is stale (" + summary + "); the re-baked fingerprint " + fingerprint + " is used.");
            }

            return fingerprint;
        }

        public string? MechanismFingerprint(string catalogType, out string? problem)
        {
            problem = null;
            Type? type = FindType(catalogType);
            if (type == null)
            {
                problem = catalogType + " is not loaded";
                return null;
            }

            FieldInfo? constant = type.GetField("CatalogFingerprint", BindingFlags.Public | BindingFlags.Static);
            string declared = (constant?.IsLiteral == true ? constant.GetRawConstantValue() : constant?.GetValue(null)) as string ?? string.Empty;
            MethodInfo? build = type.GetMethod("BuildCatalog", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (build == null || declared.Length != 64)
            {
                problem = catalogType + " has no generated CatalogFingerprint / BuildCatalog()";
                return null;
            }

            object? built;
            try
            {
                built = build.Invoke(null, null);
            }
            catch (TargetInvocationException error)
            {
                problem = catalogType + ".BuildCatalog() threw " + (error.InnerException ?? error).Message;
                return null;
            }

            ICatalog? catalog = Read<ICatalog>(built, "Catalog");
            if (catalog == null)
            {
                problem = catalogType + ".BuildCatalog() refused to build";
                return null;
            }

            string actual = catalog.Fingerprint.ToHex();
            if (!string.Equals(actual, declared, StringComparison.Ordinal))
            {
                problem = catalogType + " builds " + actual + " but declares " + declared;
                return null;
            }

            return actual;
        }

        /// <summary>The first problems of a BakeResult ("; " separated), read through its Diagnostics list.</summary>
        private static string Problems(object? result)
        {
            if (!(Read<System.Collections.IEnumerable>(result, "Diagnostics") is System.Collections.IEnumerable diagnostics))
            {
                return string.Empty;
            }

            List<string> lines = new List<string>();
            foreach (object? diagnostic in diagnostics)
            {
                if (lines.Count == 5)
                {
                    lines.Add("...");
                    break;
                }

                lines.Add(diagnostic?.ToString() ?? "(null)");
            }

            return lines.Count == 0 ? string.Empty : ": " + string.Join("; ", lines);
        }

        internal static Type? FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static T? Read<T>(object? owner, string property)
        {
            object? value = owner?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
            return value is T typed ? typed : default;
        }
    }

    /// <summary>Runs studio/stage/slot-checks.py --package-dir on the admitted package.</summary>
    public sealed class PythonAdmissionChecker : IAdmissionChecker
    {
        private readonly string? _repository;

        public PythonAdmissionChecker(string? repository)
        {
            _repository = repository;
        }

        public int TimeoutMilliseconds { get; set; } = 180000;

        public bool Check(string packageDirectory, string package, out string detail)
        {
            if (_repository == null)
            {
                detail = "the project is not inside a repository checkout, so the checkers cannot run";
                return false;
            }

            string script = Path.Combine(_repository, "studio", "stage", "slot-checks.py");
            ProcessStartInfo start = new ProcessStartInfo("python3", Quote(script) + " --package-dir " + Quote(packageDirectory) + " --package " + Quote(package) + " --repo " + Quote(_repository))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _repository,
            };
            try
            {
                using (Process process = Process.Start(start) ?? throw new InvalidOperationException("python3 did not start"))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        process.Kill();
                        detail = "the checkers did not finish in " + (TimeoutMilliseconds / 1000) + " s";
                        return false;
                    }

                    string[] lines = output.Trim().Split('\n');
                    string last = lines.Length > 0 ? lines[lines.Length - 1] : string.Empty;
                    detail = process.ExitCode == 0 ? "checkers clean: " + last : "checkers failed: " + output.Trim() + " " + errors.Trim();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is IOException)
            {
                detail = "the checkers could not run: " + error.Message;
                return false;
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
