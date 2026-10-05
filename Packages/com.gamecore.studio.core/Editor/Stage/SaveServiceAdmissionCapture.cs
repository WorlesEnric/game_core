#nullable enable
// GameCore.Studio.Edit - the P1.2 checkpoint around an admission (02 domain-reload checkpoint, P2.4 "admit flow with
// checkpoint/restore"). A SaveService is game-specific (game id, checkpoint codecs, slot schemas), so the game hands
// its running service to Studio; StageAdmission then captures to "admit-<id>" before stopping Play Mode and offers
// the slot for restore after a rollback.
//
//   StageAdmission.Of(StudioServices.Runtime).Options.Capture = new SaveServiceAdmissionCapture(() => mySaveService);
using System;
using System.IO;
using GameCore.Unity.App;

namespace GameCore.Studio.Edit
{
    /// <summary>Captures and restores through the game's <see cref="SaveService"/>.</summary>
    public sealed class SaveServiceAdmissionCapture : IAdmissionCapture
    {
        private readonly Func<SaveService?> _service;

        /// <param name="service">The running game's save service, or null while no game runs.</param>
        public SaveServiceAdmissionCapture(Func<SaveService?> service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public bool TryCapture(string slot, out string? problem)
        {
            return Run(slot, true, out problem);
        }

        public bool TryRestore(string slot, out string? problem)
        {
            return Run(slot, false, out problem);
        }

        private bool Run(string slot, bool capture, out string? problem)
        {
            if (!SaveSlotNames.IsValid(slot))
            {
                problem = SaveSlotNames.Describe(slot);
                return false;
            }

            SaveService? service = _service();
            if (service == null)
            {
                problem = "no game is running (the save service is not available)";
                return false;
            }

            // A crash can occur after Capture writes the slot but before the admission checkpoints stop-play.
            // Never replace that pre-admission world with the fresh world's state on retry.
            if (capture && (File.Exists(service.DocumentPath(slot)) || File.Exists(service.HeaderPath(slot))))
            {
                SaveSlotInspection existing = service.Inspect(slot);
                problem = existing.Readable ? null : "The existing admission checkpoint is incomplete or unreadable; it was retained.";
                return existing.Readable;
            }
            SaveResult result = capture ? service.Capture(slot) : service.Restore(slot);
            problem = result.Refusal == null ? null : result.Refusal.CodeId + ": " + result.Refusal.Detail + " (" + result.Refusal.Hint + ")";
            return result.Succeeded;
        }
    }
}
