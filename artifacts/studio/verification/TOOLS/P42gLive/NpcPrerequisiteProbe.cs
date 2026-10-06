#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Entities;
using Hollowmere.Boot;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace P42g.Live
{
    public static class NpcPrerequisiteProbe
    {
        private const string Prefix = "P42g.NpcView.";

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetString(Prefix + "output", Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!);
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.ToString("o"));
            SessionState.SetInt(Prefix + "phase", 1);
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
            EditorApplication.EnterPlaymode();
            Hook();
        }

        private static void Tick()
        {
            int phase = SessionState.GetInt(Prefix + "phase", 0);
            if (phase == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (phase == 2)
                {
                    if (EditorApplication.isPlaying) return;
                    SessionState.SetInt(Prefix + "phase", 0);
                    EditorApplication.Exit(0);
                    return;
                }
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "start", "")).ToUniversalTime()).TotalSeconds > 180)
                    throw new InvalidOperationException("actual NPC view prerequisite timeout");
                if (!EditorApplication.isPlaying) return;
                if (Application.isBatchMode) throw new InvalidOperationException("graphical Play required");
                var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
                if (boot?.Saves == null || !boot.AdmissionReady(boot.Saves) || boot.NpcExtension == null || boot.World?.Views == null)
                {
                    Write(new JObject { ["status"] = "pending", ["phase"] = "boot", ["bootExists"] = boot != null, ["failure"] = boot?.Failure ?? "absent" });
                    return;
                }
                var npc = boot.NpcExtension.Records.Single(record => record.DisplayName == "Pip");
                var world = boot.World;
                if (!world.Views.IsActive || !world.Views.TryGetView(npc.Target, out var view) || view == null)
                {
                    Write(new JObject { ["status"] = "pending", ["phase"] = "resident-view", ["npc"] = npc.DisplayName, ["viewsActive"] = world.Views.IsActive });
                    return;
                }
                var nav = view.GetComponent<NavMeshAgent>();
                if (nav == null || !nav.enabled || !nav.isOnNavMesh || !view.activeInHierarchy)
                {
                    Write(new JObject { ["status"] = "pending", ["phase"] = "navigation", ["viewActive"] = view.activeInHierarchy, ["agentPresent"] = nav != null, ["agentEnabled"] = nav != null && nav.enabled, ["onNavMesh"] = nav != null && nav.isOnNavMesh });
                    return;
                }
                var entity = AssetDatabase.LoadAssetAtPath<EntityDefinition>("Assets/Hollowmere/Npcs/Definitions/OddEntity.asset");
                if (entity?.Prefab == null) throw new InvalidOperationException("Odd entity prefab missing");
                var resident = AssetDatabase.LoadAssetAtPath<EntityDefinition>("Assets/Hollowmere/Npcs/Definitions/PipEntity.asset");
                if (resident?.Prefab != entity.Prefab) throw new InvalidOperationException("Pip and Odd do not share the observed prefab");
                var receipt = new JObject
                {
                    ["status"] = "PASS", ["isPlaying"] = true,
                    ["npcId"] = npc.AuthoringId, ["entityDefinition"] = AssetDatabase.GetAssetPath(entity),
                    ["observedNpc"] = npc.DisplayName, ["observedEntityDefinition"] = AssetDatabase.GetAssetPath(resident),
                    ["sharesRequestedPrefab"] = true,
                    ["prefab"] = AssetDatabase.GetAssetPath(entity.Prefab), ["viewName"] = view.name,
                    ["viewActive"] = view.activeInHierarchy, ["agentEnabled"] = nav.enabled, ["onNavMesh"] = nav.isOnNavMesh,
                    ["creatorDecision"] = "Reuse OddEntity's NpcCapsule prefab for Ferryman Bram. Pip uses the exact same prefab and its resident Village instance has the observed active graphical view and enabled on-NavMesh agent. No new visual asset or media generation."
                };
                Write(receipt);
                SessionState.SetInt(Prefix + "phase", 2);
                EditorApplication.ExitPlaymode();
            }
            catch (Exception error)
            {
                Write(new JObject { ["status"] = "FAIL", ["detail"] = error.ToString() });
                SessionState.SetInt(Prefix + "phase", 0);
                EditorApplication.Exit(1);
            }
        }

        private static void Write(JObject receipt) => File.WriteAllText(Path.Combine(SessionState.GetString(Prefix + "output", ""), "npc-view-prerequisite.json"), receipt.ToString());
    }
}
