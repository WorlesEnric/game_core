// Per-project creator worker preference; deliberately separate from ETOS credential settings.
#nullable enable
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    [FilePath("UserSettings/GameCoreStudio.Worker.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class StudioWorkerSettings : ScriptableSingleton<StudioWorkerSettings>
    {
        [SerializeField] private string worker = "gc-designer";
        public string Worker
        {
            get => string.IsNullOrWhiteSpace(worker) ? "gc-designer" : worker;
            set { worker = value; Save(true); }
        }
    }
}
