#nullable enable
// Hollowmere.Mechanism.PressurePlate.Editor - the mechanism's authoring operation (W-MECH-01 sample).
//
//   mechanism.pressurePlate.add   place a pressure plate in the active scene at a location
//
// Mirrors entity.place (GameCore.Gameplay.Entities.Editor.EntityTools): the tool validates its arguments before touching
// anything, records Undo for the created object and marks the scene dirty; a refused call changes nothing and throws an
// ArgumentException whose message starts with its GP-PLATE-* code.
using System;
using GameCore.Gameplay.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.Mechanism.PressurePlate.Editor
{
    /// <summary>The mechanism.pressurePlate.* authoring operations.</summary>
    public static class PressurePlateTools
    {
        /// <summary>Stable tool id of <see cref="Add"/>.</summary>
        public const string AddToolId = "mechanism.pressurePlate.add";

        [AuthorOperation(AddToolId, Tier = ToolTier.Mechanism, RuntimeApplicability = RuntimeApply.Rebuild,
            Requires = "world.region",
            Doc = "Places a pressure plate in the active region scene at a location (m), optionally with a plate definition.")]
        public static PressurePlateAuthoring Add(
            [AuthorArg(Unit = "m", Doc = "World position of the plate.")] Vector3 location,
            [AuthorArg(Category = "pressureplate.definition", Required = false,
                Doc = "The plate definition; without one the plate uses threshold 1 and maximum weight 4.")]
            PressurePlateDefinition? definition = null,
            [AuthorArg(Required = false, Doc = "Object name; defaults to \"Pressure Plate\".")] string name = "")
        {
            return AddIn(SceneManager.GetActiveScene(), location, definition, name);
        }

        /// <summary><see cref="Add"/> into an explicit scene (authoring scripts and tests).</summary>
        public static PressurePlateAuthoring AddIn(Scene scene, Vector3 location, PressurePlateDefinition? definition, string name)
        {
            if (!IsFinite(location))
            {
                throw new ArgumentException(PlateDiagnosticCodes.InvalidLocation + ": the location must be a finite position");
            }

            if (definition != null && !definition.IsValid)
            {
                throw new ArgumentException(
                    PlateDiagnosticCodes.InvalidDefinition + ": definition " + definition.name + " has threshold "
                    + definition.Threshold + " and maximum weight " + definition.MaxWeight
                    + " (threshold >= 1, maximum >= threshold, both <= " + PlateDefaults.Limit + ")");
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException(PlateDiagnosticCodes.SceneNotLoaded + ": the target scene is not loaded");
            }

            if (definition != null)
            {
                definition.EnsureAuthoringId();
            }

            var instance = new GameObject(string.IsNullOrEmpty(name) ? "Pressure Plate" : name);
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetPositionAndRotation(location, Quaternion.identity);
            PressurePlateAuthoring plate = instance.AddComponent<PressurePlateAuthoring>();
            plate.SetDefinition(definition);
            plate.EnsureAuthoringId();

            Undo.RegisterCreatedObjectUndo(instance, AddToolId);
            EditorUtility.SetDirty(plate);
            EditorSceneManager.MarkSceneDirty(scene);
            return plate;
        }

        private static bool IsFinite(Vector3 value) =>
            !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z)
              || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    }
}
