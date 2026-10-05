#nullable enable
// GameCore Studio staging harness (P2.4, studio/stage/template): shared helpers of the EditMode catalog probe and the
// PlayMode smoke runner. The harness lives only in staging slots; it never references Studio packages and reaches the
// candidate mechanism by the type names its proposal declares (StageHarness.json, written by make-slot.py).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GameCore.Stage.Harness
{
    /// <summary>StageHarness.json at the slot project root.</summary>
    [Serializable]
    public sealed class HarnessConfig
    {
        public string package = string.Empty;
        public string changeSetId = string.Empty;
        public string catalogType = string.Empty;
        public string smokeType = string.Empty;
        public string smokeMethod = "Begin";
        public int smokeSteps = 120;
        public string outDir = string.Empty;
    }

    /// <summary>Reads the configuration and writes result files into the slot's out directory.</summary>
    public static class HarnessFiles
    {
        public const string ConfigName = "StageHarness.json";

        public static HarnessConfig Load()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), ConfigName);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("no " + ConfigName + " at the project root; this project is not a staging slot");
            }

            HarnessConfig? config = JsonUtility.FromJson<HarnessConfig>(File.ReadAllText(path, Encoding.UTF8));
            if (config == null || string.IsNullOrEmpty(config.outDir))
            {
                throw new InvalidOperationException(ConfigName + " names no out directory");
            }

            return config;
        }

        public static void Write(HarnessConfig config, string name, string json)
        {
            Directory.CreateDirectory(config.outDir);
            string path = Path.Combine(config.outDir, name);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json + "\n", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        /// <summary>A loaded type by full name, or null.</summary>
        public static Type? FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type? type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The catalog-set hash (P2.4): the fingerprint of the catalog a world runs when admitted mechanisms add their own
    /// catalogs to the world's baked catalog. Lowercase hex SHA-256 of UTF-8
    /// "gamecore.catalog-set/1\n" + world + "\n" + the mechanism fingerprints sorted ordinally and joined by "\n";
    /// with no mechanism it is the world fingerprint itself. The same formula is implemented by the mechanism's
    /// CompositeCatalog and by the Studio admission.
    /// </summary>
    public static class CatalogSetHash
    {
        public const string Prefix = "gamecore.catalog-set/1";

        public static string Combine(string world, IEnumerable<string> mechanisms)
        {
            var sorted = new List<string>(mechanisms);
            if (sorted.Count == 0)
            {
                return world;
            }

            sorted.Sort(StringComparer.Ordinal);
            string text = Prefix + "\n" + world + "\n" + string.Join("\n", sorted);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                {
                    hex.Append(digest[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }
    }
}
