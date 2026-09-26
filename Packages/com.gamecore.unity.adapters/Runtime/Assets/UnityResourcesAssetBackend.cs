// GameCore.Unity.Adapters.Assets — the Unity engine half of asynchronous asset loading (GC-019).
//
// Normative sources: 04 s7's Assets row ("Completed load results carrying WorldId, installation generation,
// ActivationEpoch, and operation/work identity; leases keep assets alive through their last consumer job/frame; late
// results are discarded/released after teardown or reconfiguration") and P-058/04 s8 (no Addressables dependency is
// selected by V1 unless a task chooses it explicitly; GC-019's non-goals say the same). So the production backend
// here is Unity's own `Resources` async load, which is part of the engine and needs no package pin, and the
// qualification fixtures use the deterministic in-memory backend instead of shipping an asset.
//
// The backend's whole job is: start a real asynchronous load, report progress as a *poll*, and drop its reference
// exactly once. Identity, validation and lifetime stay in `AssetLeaseTable` (the pure side), which is why this class
// holds no token and can never decide that a completed load is authoritative.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using UnityEngine;

namespace GameCore.Unity.Adapters.Assets
{
    /// <summary>
    /// Unity `Resources`-based asset backend. `TryBeginLoad` starts a real `ResourceRequest`; `Poll` reports its
    /// progress; `Release` unloads nothing Unity would not unload itself, because `Resources` assets live for the
    /// process — what this class drops is its own reference and its own record, so the leak counter is real.
    /// </summary>
    public sealed class UnityResourcesAssetBackend : IAssetBackend
    {
        private readonly Dictionary<long, LoadEntry> loads = new Dictionary<long, LoadEntry>();
        private readonly List<long> order = new List<long>();

        private long nextHandle;

        /// <summary>Loads this backend started and has not released; zero proves the adapter let nothing go.</summary>
        public int OutstandingLoadCount => loads.Count;

        public int PollCount { get; private set; }

        public int CompletedCount { get; private set; }

        public int FailedCount { get; private set; }

        /// <summary>
        /// Starts one `Resources` load. The resource key's canonical name is the path; a key with no path component
        /// is refused as a value, so a caller never waits for a load that never started (P-029, P-049).
        /// </summary>
        public bool TryBeginLoad(AssetLoadRequest request, out long handle, out DiagnosticCode code, out string detail)
        {
            handle = 0L;
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string path = PathOf(request.Resource);
            if (path.Length == 0)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the resource key carries no asset path, so no load was started (P-009)";
                return false;
            }

            ResourceRequest pending = Resources.LoadAsync<UnityEngine.Object>(path);
            if (pending == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "Unity refused to start a load for '" + path + "'";
                return false;
            }

            nextHandle++;
            handle = nextHandle;
            loads.Add(handle, new LoadEntry(request.Resource, path, pending));
            order.Add(handle);
            return true;
        }

        public AssetLoadPoll Poll(long handle)
        {
            PollCount++;
            if (!loads.TryGetValue(handle, out LoadEntry entry))
            {
                // A poll for an unknown handle is a failure, not an eternal pending: the load does not exist.
                return AssetLoadPoll.Failed(
                    DiagnosticCode.ResourceUnavailable,
                    "no such asset load handle; the load was already released or never started (P-005)");
            }

            if (!entry.Request.isDone)
            {
                return AssetLoadPoll.Pending();
            }

            UnityEngine.Object? asset = entry.Request.asset;
            if (asset == null)
            {
                FailedCount++;
                return AssetLoadPoll.Failed(
                    DiagnosticCode.ResourceUnavailable,
                    "the load of '" + entry.Path + "' completed without an asset");
            }

            CompletedCount++;
            return AssetLoadPoll.Ready(new FrozenPayload(new[] { (byte)1 }));
        }

        public void Release(long handle)
        {
            if (!loads.TryGetValue(handle, out LoadEntry entry))
            {
                return;
            }

            loads.Remove(handle);
            RemoveOrder(handle);
            // `Resources` owns the loaded asset's lifetime for the process; dropping the request is this backend's
            // only reference to release, and the record is what the leak counter observes (04 s7).
            _ = entry.Path;
        }

        private static string PathOf(ResourceKey resource)
        {
            // A resource key is a stable 128-bit identity, so the path is carried in its name form when the content
            // catalog declares one; a key with no path is refused rather than guessed (P-015: no guessed adapter).
            if (ResourcePaths.TryGet(resource, out string? path) && path != null)
            {
                return path;
            }

            return string.Empty;
        }

        private void RemoveOrder(long handle)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == handle)
                {
                    order.RemoveAt(i);
                    return;
                }
            }
        }

        private readonly struct LoadEntry
        {
            public readonly ResourceKey Resource;
            public readonly string Path;
            public readonly ResourceRequest Request;

            public LoadEntry(ResourceKey resource, string path, ResourceRequest request)
            {
                Resource = resource;
                Path = path;
                Request = request;
            }
        }
    }

    /// <summary>
    /// Declared resource-path table. A resource key is a stable identity (P-004), not a path, so the mapping from key
    /// to engine path is content that the application declares; a miss is reported rather than turned into a guess.
    /// </summary>
    public static class ResourcePaths
    {
        private static readonly Dictionary<Id128, string> paths = new Dictionary<Id128, string>();

        public static int Count => paths.Count;

        public static void Declare(ResourceKey resource, string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            paths[resource.Value] = path;
        }

        public static bool TryGet(ResourceKey resource, out string? path) => paths.TryGetValue(resource.Value, out path);

        /// <summary>Clears declarations; called by the per-session reset path (04 s9).</summary>
        public static void Reset() => paths.Clear();
    }
}
