// Hollowmere - media for P3.1: the media manifest (artifacts/studio/workflows/P3.1/media-manifest.json), the
// procedural sound effects added to the audio bank, and the generated media - speaker portraits (generate.image) and
// the voice lines of every conversation (tts) - requested through the Studio's ETOS gateway (EtosMediaGenerator:
// max_cost_usd always sent, bytes retained in Studio/Artifacts, each file imported by a journaled asset.import).
//
// Budget: every op is sent with a ceiling (portraits 0.50 USD, voice lines 0.10 USD) and the manifest counts the
// ceilings (the gateway reports no actual cost; when the op state carries one it is recorded too). Before each call
// the sum of ceilings so far plus the next ceiling must stay within 15 USD, otherwise generation stops and the run
// reports blocked-by-budget. A file that already exists is not generated again. Without a reachable node the run
// reports the exact error and generates nothing (no silent placeholders).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Audio.Editor;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static Hollowmere.Authoring.HollowmerePaths;

namespace Hollowmere.Authoring
{
    /// <summary>Media manifest, procedural SFX and generated portraits and voices.</summary>
    public static class HollowmereMedia
    {
        public const double BudgetUsd = 15.0;
        public const double PortraitCeilingUsd = 0.50;
        public const double VoiceCeilingUsd = 0.10;
        public const string ManifestRelative = "artifacts/studio/workflows/P3.1/media-manifest.json";

        /// <summary>Speaker, TTS voice, portrait prompt.</summary>
        public static readonly (string Speaker, string Graph, string Voice, string Portrait)[] Cast =
        {
            ("Maren", "Maren", "Katerina", "Painterly portrait of Maren, a kind middle-aged village healer with grey-streaked auburn hair tied back, sage-green shawl, herbs at her collar, warm candlelight, dark muted background, storybook fantasy style, head and shoulders"),
            ("Hale", "Hale", "Ryan", "Painterly portrait of Warden Hale, a stern weathered gate warden in a dented helmet and blue-grey cloak, holding a lantern, marsh fog behind him, storybook fantasy style, head and shoulders"),
            ("Odd", "Odd", "Elias", "Painterly portrait of Odd the ferryman, a gaunt old man with a long pole over his shoulder, patched brown coat, wide-brimmed hat dripping with marsh water, green lantern glow, storybook fantasy style, head and shoulders"),
            ("Pip", "Pip", "Cherry", "Painterly portrait of Pip, a curious freckled village child with messy straw-coloured hair and a yellow scarf, bright eyes, cottage background, storybook fantasy style, head and shoulders"),
            ("Belfry Echo", "BelfryEcho", "Jennifer", "Painterly portrait of the Belfry Echo, a translucent pale-blue ghostly figure of a drowned bell-ringer, hair floating as if underwater, faint moonlight, ruined bell tower behind, storybook fantasy style, head and shoulders"),
            ("Bram", "Bram", "Ethan", "Painterly portrait of Bram the innkeeper, a broad bearded man with rolled sleeves and a red apron, holding a tankard, warm tavern firelight, storybook fantasy style, head and shoulders"),
        };

        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        public static string ManifestPath => Path.Combine(RepoRoot, ManifestRelative);

        public static string PortraitPath(string speaker) => Portraits + "/" + speaker.Replace(" ", string.Empty) + ".png";

        // ------------------------------------------------------------------ manifest

        public static JObject LoadManifest()
        {
            if (File.Exists(ManifestPath))
            {
                return JObject.Parse(File.ReadAllText(ManifestPath));
            }

            return new JObject
            {
                ["packet"] = "P3.1",
                ["budgetUsd"] = BudgetUsd,
                ["note"] = "costUsd is the max_cost_usd ceiling sent with the op (the gateway reports no actual cost); reportedCost is copied from the op state when present. origin procedural = generated locally, not by a provider.",
                ["entries"] = new JArray(),
            };
        }

        public static void SaveManifest(JObject manifest)
        {
            double ceilings = 0;
            int calls = 0;
            int procedural = 0;
            foreach (JToken entry in (JArray)manifest["entries"]!)
            {
                if ((string?)entry["origin"] == "etos")
                {
                    calls += entry.Value<int?>("calls") ?? 1;
                    ceilings += entry.Value<double?>("costUsd") ?? 0;
                }
                else
                {
                    procedural++;
                }
            }

            manifest["summary"] = new JObject
            {
                ["etosCalls"] = calls,
                ["ceilingUsdSpent"] = Math.Round(ceilings, 2),
                ["procedural"] = procedural,
                ["entries"] = ((JArray)manifest["entries"]!).Count,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(ManifestPath, manifest.ToString(Formatting.Indented) + "\n");
        }

        /// <summary>Adds or replaces the manifest entry of an asset path.</summary>
        public static void Upsert(JObject manifest, JObject entry)
        {
            var entries = (JArray)manifest["entries"]!;
            for (int i = 0; i < entries.Count; i++)
            {
                if ((string?)entries[i]["path"] == (string?)entry["path"])
                {
                    int previousCalls = entries[i].Value<int?>("calls") ?? 1;
                    double previousCost = entries[i].Value<double?>("costUsd") ?? 0;
                    if ((string?)entry["origin"] == "etos" && (string?)entries[i]["origin"] == "etos")
                    {
                        entry["calls"] = previousCalls + (entry.Value<int?>("calls") ?? 1);
                        entry["costUsd"] = previousCost + (entry.Value<double?>("costUsd") ?? 0);
                    }

                    entries[i] = entry;
                    return;
                }
            }

            entries.Add(entry);
        }

        public static void RecordProcedural(string assetPath, string op, string recipe, byte[] bytes)
        {
            JObject manifest = LoadManifest();
            Upsert(manifest, new JObject
            {
                ["path"] = assetPath,
                ["origin"] = "procedural",
                ["op"] = op,
                ["prompt"] = recipe,
                ["sha256"] = ContentStamp.Sha256Hex(bytes),
                ["bytes"] = bytes.LongLength,
                ["costUsd"] = 0,
            });
            SaveManifest(manifest);
        }

        public static double CeilingsSpent(JObject manifest)
        {
            double total = 0;
            foreach (JToken entry in (JArray)manifest["entries"]!)
            {
                if ((string?)entry["origin"] == "etos")
                {
                    total += entry.Value<double?>("costUsd") ?? 0;
                }
            }

            return total;
        }

        // ------------------------------------------------------------------ procedural SFX

        private static readonly (ProceduralClipSpec Spec, AudioGroup Group, float Volume)[] Sfx =
        {
            (new ProceduralClipSpec("sfx.pickup", "sfx_pickup.wav", ProceduralRecipe.Click, 0.16f, 71) { frequency = 880f, peak = 0.5f }, AudioGroup.Sfx, 0.9f),
            (new ProceduralClipSpec("sfx.shrine.light", "sfx_shrine_light.wav", ProceduralRecipe.Bell, 2.5f, 73) { frequency = 523.25f, decay = 2f, peak = 0.5f }, AudioGroup.Sfx, 0.8f),
            (new ProceduralClipSpec("sfx.splash", "sfx_splash.wav", ProceduralRecipe.Footstep, 0.7f, 79) { noise = 1f, noiseCutoffHz = 1800f, peak = 0.6f, decay = 5f }, AudioGroup.Sfx, 0.9f),
            (new ProceduralClipSpec("sting.quest.complete", "sting_quest_complete.wav", ProceduralRecipe.Bell, 3f, 83) { frequency = 392f, decay = 1.5f, peak = 0.6f }, AudioGroup.Music, 0.8f),
            (new ProceduralClipSpec("sfx.gate.creak", "sfx_gate_creak.wav", ProceduralRecipe.Footstep, 0.9f, 97) { noise = 0.8f, noiseCutoffHz = 420f, frequency = 110f, peak = 0.55f, decay = 3f }, AudioGroup.Sfx, 0.9f),
            (new ProceduralClipSpec("sfx.bell.toll", "sfx_bell_toll.wav", ProceduralRecipe.Bell, 5f, 101) { frequency = 147f, decay = 0.8f, peak = 0.8f }, AudioGroup.Sfx, 1f),
            (new ProceduralClipSpec("ambience.shrine", "ambience_shrine.wav", ProceduralRecipe.Ambience, 10f, 89) { tones = new[] { 261.63f, 392f, 523.25f }, toneGain = 0.22f, noise = 0.2f, noiseCutoffHz = 600f, peak = 0.4f }, AudioGroup.Ambience, 0.6f),
        };

        public const string SfxFolder = "Assets/Hollowmere/Audio/Generated/P3_1";

        public static void AuthorSfx(StudioAuthor a)
        {
            var artifacts = new List<ArtifactRef>();
            a.Step("media.sfx-import", "Import the P3.1 procedural sound effects (pickup, shrine, splash, quest sting, shrine ambience)", () =>
            {
                var ops = new List<Operation>();
                for (int i = 0; i < Sfx.Length; i++)
                {
                    ProceduralClipSpec spec = Sfx[i].Spec;
                    byte[] wav = ProceduralAudioGenerator.Wav(ProceduralAudioGenerator.Render(spec), ProceduralAudioGenerator.DefaultSampleRate);
                    ArtifactRef artifact = a.Retain(wav, "audio/wav", spec.file, "procedural.audio", "sfx");
                    artifacts.Add(artifact);
                    RecordProcedural(SfxFolder + "/" + spec.file, "procedural.audio", "ProceduralAudioGenerator " + spec.recipe + " " + spec.seconds.ToString(CultureInfo.InvariantCulture) + " s seed " + spec.seed, wav);
                    ops.Add(StudioAuthor.Import("a" + i, SfxFolder + "/" + spec.file, artifact));
                }

                return ops;
            }, artifacts);

            a.Step("media.sfx-bank", "Put the P3.1 sound effects into the Hollowmere audio bank", () =>
            {
                AuthoringRef bank = a.Ref(AudioBank);
                var ops = new List<Operation>();
                for (int i = 0; i < Sfx.Length; i++)
                {
                    ProceduralClipSpec spec = Sfx[i].Spec;
                    ops.Add(StudioAuthor.Call("b" + i, "audio.assignClip", bank, new JObject
                    {
                        ["clipId"] = spec.id,
                        ["clip"] = SfxFolder + "/" + spec.file,
                        ["group"] = Sfx[i].Group.ToString(),
                        ["volume"] = Sfx[i].Volume,
                        ["loop"] = spec.loop,
                        ["spatial"] = false,
                    }));
                }

                return ops;
            });
        }

        // ------------------------------------------------------------------ generated media

        /// <summary>Outcome of a generation run.</summary>
        public sealed class Report
        {
            public int Calls;
            public int Imported;
            public int Skipped;
            public double CeilingUsd;
            public string? Blocked;
            public List<string> Failures { get; } = new List<string>();
        }

        /// <summary>Generates the missing portraits and voice lines (synchronously, pumping the gateway's main-thread queue).</summary>
        public static Report Generate(StudioRuntime runtime, bool portraits, bool voices, int maxCalls)
        {
            var report = new Report();
            JObject manifest = LoadManifest();
            EtosSettings settings = EtosSettings.Load(runtime.Paths.ProjectRoot);
            if (!settings.IsConfigured)
            {
                report.Blocked = EtosCodes.NotConfigured + ": no app key file (" + EtosCredentials.KeyFileVariable + " or the pairing default)";
                return report;
            }

            EtosCredentials credentials;
            try
            {
                credentials = settings.ReadCredentials();
            }
            catch (EtosException error)
            {
                report.Blocked = EtosRedaction.Redact("credentials: " + error.Error);
                return report;
            }

            var log = new RedactingStudioLog(runtime.Log);
            using (var client = new CompanionClient(settings.ToClientOptions(credentials, line => log.Write(StudioLogLevel.Debug, "etos", line)), credentials))
            {
                var queue = new MainThreadQueue(log);
                using (var gateway = new EtosAgentGateway(client, runtime, queue, new MemoryCursorStore(), new EtosGatewayOptions { MaxCostUsd = PortraitCeilingUsd }, log))
                {
                    gateway.Start();
                    var media = new EtosMediaGenerator(gateway, runtime, queue);
                    var jobs = new List<(string Path, string Op, string Prompt, string? Voice, double Ceiling, string Speaker)>();
                    foreach (var member in Cast)
                    {
                        if (portraits)
                        {
                            jobs.Add((PortraitPath(member.Speaker), "generate.image", member.Portrait, null, PortraitCeilingUsd, member.Speaker));
                        }

                        if (voices)
                        {
                            foreach (var line in HollowmereDialogues.Build(member.Graph, false).Lines)
                            {
                                jobs.Add((GraphBuilder.VoicePath(member.Graph, line.Key), "tts", line.Text, member.Voice, VoiceCeilingUsd, member.Speaker));
                            }
                        }
                    }

                    foreach (var job in jobs)
                    {
                        if (File.Exists(Path.Combine(runtime.Paths.ProjectRoot, job.Path)))
                        {
                            report.Skipped++;
                            continue;
                        }

                        if (report.Calls >= maxCalls)
                        {
                            report.Blocked = "stopped after " + maxCalls + " calls (-mediaMaxCalls)";
                            break;
                        }

                        double spent = CeilingsSpent(manifest);
                        if (spent + job.Ceiling > BudgetUsd + 1e-9)
                        {
                            report.Blocked = "blocked-by-budget: " + spent.ToString("F2", CultureInfo.InvariantCulture) + " USD of ceilings spent, the next op would pass " + BudgetUsd + " USD";
                            break;
                        }

                        Task<MediaImport> task = job.Op == "tts"
                            ? media.GenerateSpeechAsync(job.Prompt, job.Path, job.Voice, job.Ceiling)
                            : media.GenerateImageAsync(job.Prompt, job.Path, 256, job.Ceiling);
                        MediaImport? result = Wait(task, queue, job.Op == "tts" ? 180 : 300, out string? waitError);
                        report.Calls++;
                        report.CeilingUsd += job.Ceiling;
                        var entry = new JObject
                        {
                            ["path"] = job.Path,
                            ["origin"] = "etos",
                            ["op"] = job.Op,
                            ["prompt"] = job.Prompt,
                            ["speaker"] = job.Speaker,
                            ["voice"] = job.Voice,
                            ["costUsd"] = job.Ceiling,
                            ["calls"] = 1,
                        };
                        if (result != null && result.Artifact != null && result.Report != null && result.Report.Ok)
                        {
                            entry["sha256"] = result.Artifact.Sha256;
                            entry["bytes"] = result.Artifact.Bytes;
                            entry["provider"] = result.Result.Provider;
                            entry["key"] = result.Result.Name;
                            entry["journalState"] = result.Report.State.ToString();
                            entry["reportedCost"] = CostOf(result.Result.State);
                            report.Imported++;
                        }
                        else
                        {
                            string problem = waitError ?? Describe(result);
                            entry["error"] = EtosRedaction.Redact(problem);
                            report.Failures.Add(job.Path + ": " + EtosRedaction.Redact(problem));
                        }

                        Upsert(manifest, entry);
                        SaveManifest(manifest);
                        if (report.Failures.Count >= 3 && report.Imported == 0)
                        {
                            report.Blocked = "the first three ops failed; last: " + report.Failures[report.Failures.Count - 1];
                            break;
                        }
                    }
                }
            }

            return report;
        }

        private static JToken? CostOf(JToken? state)
        {
            if (state is JObject o)
            {
                foreach (string name in new[] { "cost_usd", "costUsd", "cost" })
                {
                    if (o[name] != null)
                    {
                        return o[name];
                    }
                }
            }

            return null;
        }

        private static string Describe(MediaImport? result)
        {
            if (result == null)
            {
                return "no result";
            }

            var parts = new List<string>();
            foreach (Diagnostic d in result.Diagnostics)
            {
                parts.Add(d.Code + ": " + d.Message);
            }

            if (result.Result.Refusal != null)
            {
                parts.Add(result.Result.Refusal.Code + ": " + result.Result.Refusal.Message);
            }

            return parts.Count == 0 ? "not imported" : string.Join("; ", parts);
        }

        private static T? Wait<T>(Task<T> task, MainThreadQueue queue, double seconds, out string? error)
            where T : class
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (!task.IsCompleted)
            {
                queue.Pump();
                if (DateTime.UtcNow > end)
                {
                    error = "timed out after " + seconds + " s";
                    return null;
                }

                Thread.Sleep(15);
            }

            queue.Pump();
            if (task.IsFaulted)
            {
                error = task.Exception?.GetBaseException().Message ?? "faulted";
                return null;
            }

            error = null;
            return task.Result;
        }

        /// <summary>After generation: portraits onto the director, voice clips onto the dialogue lines (journaled steps).</summary>
        public static void AttachGenerated(StudioAuthor a)
        {
            var portraits = new JArray();
            foreach (var member in Cast)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPath(member.Speaker)) != null)
                {
                    portraits.Add(new JObject { ["speaker"] = member.Speaker, ["portrait"] = PortraitPath(member.Speaker) });
                }
            }

            if (portraits.Count > 0)
            {
                a.Step("media.portraits-" + portraits.Count, "Show the " + portraits.Count + " generated speaker portraits in the dialogue panel", () => new List<Operation>
                {
                    StudioAuthor.Set("portraits", a.Ref(Director), new JObject { ["portraits"] = portraits }),
                });
            }

            var voices = new List<AudioClip>();
            foreach (var member in Cast)
            {
                foreach (var line in HollowmereDialogues.Build(member.Graph, false).Lines)
                {
                    AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(GraphBuilder.VoicePath(member.Graph, line.Key));
                    if (clip != null)
                    {
                        voices.Add(clip);
                    }
                }
            }

            if (voices.Count > 0 && a.NarrativeBlocked == null)
            {
                HollowmereWorldContent.Graphs(a, true, voices.Count);
                a.Step("media.voices-bank-" + voices.Count, "Enroll the generated dialogue voices in the Hollowmere audio bank", () =>
                {
                    AuthoringRef bank = a.Ref(AudioBank);
                    var ops = new List<Operation>();
                    for (int i = 0; i < voices.Count; i++)
                    {
                        AudioClip clip = voices[i];
                        ops.Add(StudioAuthor.Call("v" + i, "audio.assignClip", bank, new JObject
                        {
                            ["clipId"] = clip.name,
                            ["clip"] = AssetDatabase.GetAssetPath(clip),
                            ["group"] = AudioGroup.Voice.ToString(),
                            ["volume"] = 1f,
                            ["loop"] = false,
                            ["spatial"] = false,
                        }));
                    }

                    return ops;
                });
            }
        }
    }
}
