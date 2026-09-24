// API snapshot freeze test (GC-002). Regenerates the reference-seam listing in-process and compares it with
// the committed file, so an unintended public-surface change fails the W0 gate.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.ApiSnapshot;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.ReferenceSeams.Tests
{
    [TestFixture]
    public sealed class ApiSnapshotTests
    {
        private const string SurfaceNamespace = "GameCore.Contracts";

        private const string RegenerationHint =
            "Regenerate on the build host with:\n" +
            "  dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- " +
            "--assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll " +
            "--output tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt --namespace GameCore.Contracts";

        [Test]
        public void SeamApiSnapshotMatchesCommittedFile()
        {
            string root = RepoLayout.FindRoot();
            string snapshotPath = RepoLayout.Resolve(root, RepoLayout.ApiSnapshotPath);
            Assert.That(File.Exists(snapshotPath), Is.True, "Committed API snapshot is missing: " + snapshotPath);

            string committed = File.ReadAllText(snapshotPath);
            string generated = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);
            ApiSnapshotComparison comparison = ApiSnapshotDiff.Compare(committed, generated);

            if (comparison.Pending)
            {
                Assert.Fail(
                    "Committed API snapshot still carries the placeholder header '" +
                    ApiSnapshotGenerator.PendingHeader + "', so no seam surface has been frozen yet. " +
                    "The generated listing has " + comparison.ActualLines + " lines.\n" + RegenerationHint);
            }

            Assert.That(
                comparison.Equal,
                Is.True,
                "Reference seam API surface differs from the committed snapshot.\n" + comparison.Diff + "\n" + RegenerationHint);
        }

        [Test]
        public void GeneratedListingIsDeterministicAndExcludesAssemblyName()
        {
            string first = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);
            string second = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);

            Assert.That(second, Is.EqualTo(first), "Snapshot generation must be deterministic for the same input assembly.");
            Assert.That(first, Does.Contain("type struct GameCore.Contracts.Id128"));

            foreach (string line in ApiSnapshotDiff.Normalize(first).Split('\n'))
            {
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(
                    line,
                    Does.Not.Contain(typeof(Id128).Assembly.GetName().Name!),
                    "The listing body must not contain the assembly name.");
            }

            Assert.That(
                first,
                Does.Not.Contain("GameCore.TestFixtures"),
                "Deterministic stub types must stay outside the frozen GameCore.Contracts surface.");
        }

        [Test]
        public void PendingHeaderIsTreatedAsFailureWithClearMessage()
        {
            ApiSnapshotComparison comparison = ApiSnapshotDiff.Compare(
                ApiSnapshotGenerator.PendingHeader + "\n",
                "type struct GameCore.Contracts.Id128\n");

            Assert.That(comparison.Pending, Is.True);
            Assert.That(comparison.Equal, Is.False);
            Assert.That(comparison.Diff, Is.Not.Empty);
        }
    }
}
