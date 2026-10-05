#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using NUnit.Framework;
using Saltmarsh.Authoring;
using UnityEditor;
using static Saltmarsh.Authoring.SaltmarshAuthoring;
namespace Saltmarsh.Tests
{
    public sealed class CleanProofAuthoringTests
    {
        [Test]
        public void W_CLEAN_01_AuthorAllIsIdempotentAndJournaled()
        {
            AuthorAll();
            var before=Snapshot();
            int journal=Directory.GetFiles("Studio/History","*.json",SearchOption.AllDirectories).Length;
            AuthorAll();
            Assert.That(Snapshot(),Is.EqualTo(before),"a repeated authoring pass changes no authored asset or generated catalog bytes");
            var records=Directory.GetFiles("Studio/History","*.json",SearchOption.AllDirectories);
            Assert.That(records.Length,Is.EqualTo(journal+5),"all five phases use ChangeSetEngine.Apply");
            foreach(string file in records.OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
            {
                var row=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                Assert.That((string?)row["state"],Is.EqualTo("Applied"),file);
            }
            Assert.That(AssetDatabase.FindAssets("t:RegionDefinition",new[]{Root}).Length,Is.EqualTo(3));
            Assert.That(AssetDatabase.FindAssets("t:NpcDefinition",new[]{Root}).Length,Is.EqualTo(3));
            Assert.That(AssetDatabase.FindAssets("t:ItemDefinition",new[]{Root}).Length,Is.EqualTo(4));
        }
        [Test]
        public void W_CLEAN_02_BakeVerifyDetectsStaleOutput()
        {
            var world=Load<WorldDefinition>(WorldPath); var paths=BakePaths.ConventionFor(WorldPath);
            Assert.That(Entry.Verify(world,paths).Succeeded,Is.True);
            byte[] original=File.ReadAllBytes(paths.ReportPath);
            try { File.AppendAllText(paths.ReportPath," "); Assert.That(Entry.Verify(world,paths).Succeeded,Is.False,"tampering must invalidate the bake"); }
            finally { File.WriteAllBytes(paths.ReportPath,original); }
            Assert.That(Entry.Verify(world,paths).Succeeded,Is.True);
        }
        private static string[] Snapshot()
        {
            using var hash=SHA256.Create();
            return Directory.GetFiles("Assets","*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal)
                .Select(p=>p+":"+BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(p)))).ToArray();
        }
    }
}
