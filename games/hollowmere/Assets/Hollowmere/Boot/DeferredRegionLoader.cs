#nullable enable
using System;
using GameCore.Gameplay.World;

namespace Hollowmere.Boot
{
    /// <summary>Lets the menu present before first-region IO; residency still belongs to RegionStreamer.</summary>
    public sealed class DeferredRegionLoader : ISceneLoader
    {
        private readonly ISceneLoader inner;
        private readonly Func<int> frame;
        private readonly int earliest;

        public DeferredRegionLoader(ISceneLoader inner, Func<int> frame, int earliest)
        {
            this.inner = inner;
            this.frame = frame;
            this.earliest = earliest;
        }

        public ISceneOperation Load(string scenePath) => new Deferred(this, scenePath);
        public ISceneOperation Unload(string scenePath) => inner.Unload(scenePath);
        public bool IsLoaded(string scenePath) => inner.IsLoaded(scenePath);

        private sealed class Deferred : ISceneOperation
        {
            private readonly DeferredRegionLoader loader;
            private readonly string path;
            private ISceneOperation? operation;
            public Deferred(DeferredRegionLoader loader, string path) { this.loader = loader; this.path = path; }
            public bool IsDone
            {
                get
                {
                    if (loader.frame() < loader.earliest) return false;
                    operation ??= loader.inner.Load(path);
                    return operation.IsDone;
                }
            }
            public bool Failed => operation != null && operation.Failed;
        }
    }
}
