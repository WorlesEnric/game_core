// Production-contract fixture run setup (GC-003).
// One shared test source runs twice, once against the W0 reference seam and once against production
// GameCore.Contracts. Both runs write evidence, so this assembly redirects its own result document name before
// any fixture executes; the reference-seam run keeps the default name.
#nullable enable
using System;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.ProtocolFixtures.Tests
{
    /// <summary>Sets this assembly's evidence file name before the fixture suite loads its data.</summary>
    [SetUpFixture]
    public sealed class ProductionRunResultPath
    {
        /// <summary>Evidence file this run writes; kept distinct from the reference-seam run's document.</summary>
        public const string ResultName = "results-production-contracts.json";

        [OneTimeSetUp]
        public void RedirectResultDocument()
        {
            Environment.SetEnvironmentVariable(RepoLayout.ResultNameEnvironmentVariable, ResultName);
        }

        [OneTimeTearDown]
        public void RestoreResultDocument()
        {
            Environment.SetEnvironmentVariable(RepoLayout.ResultNameEnvironmentVariable, null);
        }
    }
}
