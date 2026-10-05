#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    // No AuthorOperation attributes: neither mutation is discoverable or invocable by the worker catalog.
    public static class MechanismAdmission
    {
        public const string AdmitTool = "mechanism.admit";
        public const string RemoveTool = "mechanism.remove";
        public const string ValidatorId = "RequiresStageVerdict";
        public const string EmbeddedPolicy = "embedded";
        public const string SharedPolicy = "shared";

        internal static string? Digest(JToken? value)
        {
            string? reference = value is JObject obj ? (string?)obj["artifact"] : value?.Type == JTokenType.String ? value.Value<string>() : null;
            if (reference == null) return null;
            string digest = reference.StartsWith(ContentStamp.Prefix, StringComparison.Ordinal) ? reference.Substring(ContentStamp.Prefix.Length) : reference;
            return ContentStamp.IsValidHex(digest) ? digest : null;
        }

        internal static void Install(string directory, PackageArchive archive)
        {
            string staging = directory + ".admitting";
            StageAdmission.DeleteDirectory(staging);
            Directory.CreateDirectory(staging);
            foreach (KeyValuePair<string, byte[]> file in archive.Files)
            {
                string path = StageDataPaths.ContainedFile(staging, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(file.Value, 0, file.Value.Length);
                    stream.Flush(true);
                }
            }
            Directory.Move(staging, directory);
        }
    }
}
