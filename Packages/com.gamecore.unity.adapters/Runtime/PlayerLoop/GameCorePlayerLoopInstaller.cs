#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// Dedicated marker type of the application-owned pump node. The installer identifies its own node by this
    /// type, so it never overwrites the loop with a cached default and never removes unrelated nodes (04 s3).
    /// </summary>
    public static class GameCorePumpLoop
    {
    }

    /// <summary>
    /// Idempotent install/removal of the single GameCore PlayerLoop node. It edits the <b>current</b> loop
    /// recursively, inserts exactly one node before <c>Update.ScriptRunBehaviourUpdate</c>, and never uses
    /// <c>ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop</c>: the owned worlds are driven only by this
    /// application node (04 s3).
    /// The quit hook (<see cref="InstallQuitHook"/>) exists because <c>Application.Quit</c> is deferred to the end
    /// of the frame, and a pump node that outlives the engine's world/native teardown would step a world whose
    /// storage is being released. The hook is idempotent and never removes unrelated nodes.
    /// </summary>
    public static class GameCorePlayerLoopInstaller
    {
        /// <summary>
        /// Install generation. A node installed in an earlier session carries a trampoline that refuses to pump
        /// once the generation moved, which is the domain-reload-disabled safety net beside structural removal
        /// (04 s9).
        /// </summary>
        public static int Generation { get; private set; }

        public static int InstallCount { get; private set; }

        public static int RemoveCount { get; private set; }

        /// <summary>Installations that first had to remove a node left by an earlier session.</summary>
        public static int DuplicateInstallRefusalCount { get; private set; }

        /// <summary>First-time subscriptions of the single application-quit hook; exactly one is expected.</summary>
        public static int QuitHookInstallCount { get; private set; }

        /// <summary>Times the application-quit hook has run.</summary>
        public static int QuitHookFireCount { get; private set; }

        /// <summary>Pump nodes the application-quit hook removed.</summary>
        public static int QuitRemovalCount { get; private set; }

        /// <summary>True once the single application-quit hook is subscribed.</summary>
        public static bool IsQuitHookInstalled => quitHookInstalled;

        private static bool quitHookInstalled;

        public static bool IsInstalled() => CountInstalledNodes() > 0;

        /// <summary>Counts GameCore pump nodes anywhere in the current loop.</summary>
        public static int CountInstalledNodes()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            return CountNodes(ref loop);
        }

        /// <summary>
        /// Ensures exactly one GameCore pump node exists, before <c>ScriptRunBehaviourUpdate</c> in the
        /// <c>Update</c> phase. Repeated calls are idempotent (04 s3).
        /// </summary>
        public static int EnsureInstalled()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();

            int removed = RemoveNodes(ref loop);
            if (removed > 0)
            {
                DuplicateInstallRefusalCount++;
            }

            int generation = Generation;
            var node = new PlayerLoopSystem
            {
                type = typeof(GameCorePumpLoop),
                updateDelegate = () => GameCoreApplicationPump.PumpTrampoline(generation),
            };

            if (!InsertBeforeScriptRunBehaviourUpdate(ref loop, node))
            {
                // Without an Update phase there is nothing to order against; appending keeps exactly one route.
                loop.subSystemList = Append(loop.subSystemList, node);
            }

            PlayerLoop.SetPlayerLoop(loop);
            InstallCount++;
            return CountInstalledNodes();
        }

        /// <summary>Removes every GameCore pump node from the current loop and returns how many were removed.</summary>
        public static int Remove()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            int removed = RemoveNodes(ref loop);
            if (removed > 0)
            {
                PlayerLoop.SetPlayerLoop(loop);
                RemoveCount++;
            }

            return removed;
        }

        /// <summary>
        /// Subscribes the single application-quit hook exactly once. <c>Application.quitting</c> is raised while the
        /// PlayerLoop is still intact, so the hook detaches the pump node before the engine releases its worlds and
        /// native memory. A repeated call never double-subscribes.
        /// </summary>
        public static void InstallQuitHook()
        {
            if (quitHookInstalled)
            {
                return;
            }

            Application.quitting += OnApplicationQuitting;
            quitHookInstalled = true;
            QuitHookInstallCount++;
        }

        private static void OnApplicationQuitting()
        {
            QuitHookFireCount++;

            // No owned world may be pumped once the engine has begun tearing down: a node that stepped a world
            // whose storage is being released would corrupt memory.
            GameCoreApplicationPump.IsEnabled = false;
            QuitRemovalCount += Remove();
        }

        /// <summary>Invalidates delegates installed before this call; they refuse to pump afterwards (04 s9).</summary>
        public static void BumpGeneration() => Generation++;

        internal static void ResetCounters()
        {
            InstallCount = 0;
            RemoveCount = 0;
            DuplicateInstallRefusalCount = 0;
            QuitHookInstallCount = 0;
            QuitHookFireCount = 0;
            QuitRemovalCount = 0;
            // quitHookInstalled is deliberately NOT cleared: the Application.quitting subscription survives a
            // domain-reload-disabled session, so clearing the flag would let a later call double-subscribe.
        }

        private static bool InsertBeforeScriptRunBehaviourUpdate(ref PlayerLoopSystem loop, PlayerLoopSystem node)
        {
            if (loop.subSystemList == null)
            {
                return false;
            }

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(GameCorePumpLoop))
                {
                    return true;
                }

                if (loop.subSystemList[i].type == typeof(Update))
                {
                    InsertIntoUpdatePhase(ref loop.subSystemList[i], node);
                    return true;
                }

                if (InsertBeforeScriptRunBehaviourUpdate(ref loop.subSystemList[i], node))
                {
                    return true;
                }
            }

            return false;
        }

        private static void InsertIntoUpdatePhase(ref PlayerLoopSystem updatePhase, PlayerLoopSystem node)
        {
            PlayerLoopSystem[] children = updatePhase.subSystemList ?? Array.Empty<PlayerLoopSystem>();

            int insertAt = children.Length;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == typeof(Update.ScriptRunBehaviourUpdate))
                {
                    insertAt = i;
                    break;
                }
            }

            var grown = new PlayerLoopSystem[children.Length + 1];
            for (int i = 0; i < insertAt; i++)
            {
                grown[i] = children[i];
            }

            grown[insertAt] = node;
            for (int i = insertAt; i < children.Length; i++)
            {
                grown[i + 1] = children[i];
            }

            updatePhase.subSystemList = grown;
        }

        private static PlayerLoopSystem[] Append(PlayerLoopSystem[]? children, PlayerLoopSystem node)
        {
            PlayerLoopSystem[] existing = children ?? Array.Empty<PlayerLoopSystem>();
            var grown = new PlayerLoopSystem[existing.Length + 1];
            for (int i = 0; i < existing.Length; i++)
            {
                grown[i] = existing[i];
            }

            grown[existing.Length] = node;
            return grown;
        }

        private static int CountNodes(ref PlayerLoopSystem loop)
        {
            int count = loop.type == typeof(GameCorePumpLoop) ? 1 : 0;
            if (loop.subSystemList == null)
            {
                return count;
            }

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                count += CountNodes(ref loop.subSystemList[i]);
            }

            return count;
        }

        private static int RemoveNodes(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null)
            {
                return 0;
            }

            var kept = new List<PlayerLoopSystem>(loop.subSystemList.Length);
            int removed = 0;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = loop.subSystemList[i];
                if (child.type == typeof(GameCorePumpLoop))
                {
                    removed++;
                    continue;
                }

                removed += RemoveNodes(ref child);
                kept.Add(child);
            }

            if (removed > 0)
            {
                loop.subSystemList = kept.ToArray();
            }

            return removed;
        }
    }
}
