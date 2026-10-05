// GameCore.Studio.Edit - the project's Studio runtime holder (a ScriptableSingleton: the only place Studio keeps
// editor-session state). The runtime itself is rebuilt after every domain reload from durable state: the journal
// (Studio/History), the redo stack and the index cache (Library/GameCoreStudio). The serialized session fields record
// what the previous domain knew (index revision, reload count) so a rebuilt runtime can continue the revision sequence
// and tests can observe that the session survived a reload.
#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Owner of the project's <see cref="StudioRuntime"/>.</summary>
    [FilePath("Library/GameCoreStudio/session.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class StudioServices : ScriptableSingleton<StudioServices>
    {
        [SerializeField]
        private long lastIndexRevision;

        [SerializeField]
        private int domainReloads;

        [SerializeField]
        private string lastAppliedChangeSet = string.Empty;

        [NonSerialized]
        private StudioRuntime? _runtime;

        /// <summary>The project's runtime (created on first use, after each domain reload).</summary>
        public static StudioRuntime Runtime => instance.GetOrCreate();

        /// <summary>True when the project's runtime exists in this domain (triggers do not create it during imports).</summary>
        public static bool HasRuntime => instance._runtime != null;

        /// <summary>The index revision recorded before the last domain reload.</summary>
        public long LastIndexRevision => lastIndexRevision;

        /// <summary>Domain reloads this session has survived.</summary>
        public int DomainReloads => domainReloads;

        /// <summary>The last change set the project's runtime applied.</summary>
        public string LastAppliedChangeSet => lastAppliedChangeSet;

        /// <summary>Records the index revision and saves the index cache (before a domain reload or quit).</summary>
        internal void Persist(bool countReload)
        {
            if (_runtime != null)
            {
                try
                {
                    _runtime.Index.SaveCache();
                    lastIndexRevision = _runtime.Index.Revision;
                }
                catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
                {
                    _runtime.Log.Write(Authoring.StudioLogLevel.Warning, "services", "index cache not saved: " + error.Message);
                }

                _runtime.Dispose();
                _runtime = null;
            }

            if (countReload)
            {
                domainReloads++;
            }

            Save(true);
        }

        private StudioRuntime GetOrCreate()
        {
            if (_runtime == null)
            {
                _runtime = StudioRuntime.Create();
                _runtime.Engine.Applied += report =>
                {
                    lastAppliedChangeSet = report.Entry.Id;
                    Save(true);
                };
            }

            return _runtime;
        }
    }
}
