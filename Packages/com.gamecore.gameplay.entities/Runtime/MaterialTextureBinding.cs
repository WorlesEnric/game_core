#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Presentation-only texture override, retained in the definition and reapplied on every view spawn.</summary>
    [Serializable]
    public sealed class MaterialTextureBinding
    {
        public const string InvalidCode = GameplayDiagnosticCodes.EntityMaterialTextureInvalid;

        [AuthorField(Min = 0, Doc = "Depth-first renderer index, including inactive children.")]
        public int renderer;

        [AuthorField(Min = 0, Doc = "Material slot index.")]
        public int slot;

        [AuthorField(Doc = "Shader Texture2D property.")]
        public string property = "_BaseMap";

        [AuthorRef(Category = "texture.texture2d", Doc = "Imported image asset.")]
        public Texture2D? texture;

        public static string Validate(EntityDefinition definition, MaterialTextureBinding binding)
        {
            string problem = ValidatePrefab(definition.Prefab, binding);
            if (problem.Length > 0) return problem;
            foreach (VariantDefinition variant in definition.Variants)
            {
                if (variant == null || variant.Prefab == null) continue;
                problem = ValidatePrefab(variant.Prefab, binding);
                if (problem.Length > 0) return "variant " + variant.name + ": " + problem;
            }
            return string.Empty;
        }

        private static string ValidatePrefab(GameObject? prefab, MaterialTextureBinding binding)
        {
            if (prefab == null) return "definition needs a view prefab";
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (binding.renderer < 0 || binding.renderer >= renderers.Length) return "renderer index is outside the prefab";
            Material[] materials = renderers[binding.renderer].sharedMaterials;
            if (binding.slot < 0 || binding.slot >= materials.Length) return "material slot is outside the renderer";
            Material material = materials[binding.slot];
            if (material == null || material.shader == null) return "material slot needs a shader";
            int index = material.shader.FindPropertyIndex(binding.property ?? string.Empty);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Texture
                || material.shader.GetPropertyTextureDimension(index) != TextureDimension.Tex2D)
                return "property must name a Texture2D shader property";
            return string.Empty;
        }

        /// <summary>Writes per-material property blocks without mutating shared materials or creating material assets.</summary>
        public static void Apply(GameObject view, IReadOnlyList<MaterialTextureBinding> bindings)
        {
            if (bindings.Count == 0) return;
            Renderer[] renderers = view.GetComponentsInChildren<Renderer>(true);
            var block = new MaterialPropertyBlock();
            foreach (MaterialTextureBinding binding in bindings)
            {
                if (binding.texture == null || ValidatePrefab(view, binding).Length > 0) continue;
                Renderer target = renderers[binding.renderer];
                block.Clear();
                target.GetPropertyBlock(block, binding.slot);
                if (block.isEmpty) target.GetPropertyBlock(block);
                block.SetTexture(binding.property, binding.texture);
                target.SetPropertyBlock(block, binding.slot);
            }
        }
    }
}
