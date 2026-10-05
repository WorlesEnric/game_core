#nullable enable
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    /// <summary>Candidate byte authority. Checked at stage and immediately before retaining/writing destination bytes.</summary>
    public static class MediaImportPolicy
    {
        public static string Code(string problem)
        {
            if (problem.StartsWith("media_type_", StringComparison.Ordinal)) return DiagnosticCodes.MediaTypeForbidden;
            if (problem.StartsWith("media_path_", StringComparison.Ordinal)) return DiagnosticCodes.MediaPathForbidden;
            return DiagnosticCodes.MediaImporterInvalid;
        }

        public static string? Validate(StudioPaths paths, string? path, JObject? settings)
        {
            if (!ToolSupport.IsSafeAssetPath(path) || path == null || path.Contains("\\"))
                return "media_path_forbidden: destination must be contained in Assets.";
            foreach (string segment in path.Split('/'))
                if (string.Equals(segment, "Editor", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "Plugins", StringComparison.OrdinalIgnoreCase))
                    return "media_path_forbidden: executable/import hook directories are forbidden.";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Type? type;
            switch (ext)
            {
                case ".png": case ".jpg": case ".jpeg": type = typeof(TextureImporter); break;
                case ".wav": case ".ogg": case ".mp3": type = typeof(AudioImporter); break;
                case ".fbx": type = typeof(ModelImporter); break;
                case ".json": case ".txt": case ".csv": type = null; break;
                default: return "media_type_forbidden: unsupported or executable raw asset type.";
            }
            for (string? full = paths.Absolute(path); full != null && full.Length >= paths.Absolute("Assets").Length; full = Path.GetDirectoryName(full))
                if ((File.Exists(full) || Directory.Exists(full)) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    return "media_path_forbidden: linked destinations are forbidden.";
            string meta = paths.Absolute(path) + ".meta";
            if (File.Exists(meta) && (File.GetAttributes(meta) & FileAttributes.ReparsePoint) != 0)
                return "media_path_forbidden: linked metadata is forbidden.";
            AssetImporter? existing = AssetImporter.GetAtPath(path);
            if (existing != null && type != null && existing.GetType() != type)
                return "media_importer_forbidden: custom importer is not allowed.";
            if (settings == null) return null;
            foreach (JProperty setting in settings.Properties())
            {
                bool allowed = type == typeof(TextureImporter) && (setting.Name == "textureType" || setting.Name == "sRGBTexture" || setting.Name == "alphaIsTransparency" || setting.Name == "mipmapEnabled" || setting.Name == "isReadable" || setting.Name == "maxTextureSize" || setting.Name == "filterMode" || setting.Name == "wrapMode")
                    || type == typeof(AudioImporter) && (setting.Name == "forceToMono" || setting.Name == "loadInBackground" || setting.Name == "preloadAudioData")
                    || type == typeof(ModelImporter) && (setting.Name == "globalScale" || setting.Name == "isReadable");
                if (!allowed) return "media_importer_invalid: unsupported setting " + setting.Name;
                PropertyInfo? property = type!.GetProperty(setting.Name);
                if (property == null || !property.CanWrite) return "media_importer_invalid: unavailable setting " + setting.Name;
                try
                {
                    if (property.PropertyType.IsEnum)
                    {
                        if (setting.Value.Type != JTokenType.String || !Enum.IsDefined(property.PropertyType, (string)setting.Value!))
                            return "media_importer_invalid: invalid enum " + setting.Name;
                    }
                    else if (property.PropertyType == typeof(bool))
                    {
                        if (setting.Value.Type != JTokenType.Boolean) return "media_importer_invalid: expected boolean " + setting.Name;
                    }
                    else
                    {
                        if (setting.Value.Type != JTokenType.Integer && setting.Value.Type != JTokenType.Float) return "media_importer_invalid: expected number " + setting.Name;
                        double number = setting.Value.Value<double>();
                        if (double.IsNaN(number) || double.IsInfinity(number) || number <= 0 || number > 16384)
                            return "media_importer_invalid: number outside supported range " + setting.Name;
                        if (setting.Name == "maxTextureSize" && (number < 32 || number != (int)number || (((int)number & ((int)number - 1)) != 0)))
                            return "media_importer_invalid: texture size must be a power of two from 32 to 16384.";
                    }
                }
                catch (Exception error) when (error is ArgumentException || error is FormatException || error is InvalidCastException)
                { return "media_importer_invalid: invalid value " + setting.Name; }
            }
            return null;
        }
    }
}
