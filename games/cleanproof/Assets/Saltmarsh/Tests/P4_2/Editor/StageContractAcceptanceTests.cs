#nullable enable
using System;
using System.Linq;
using GameCore.Studio.Etos.Client;
using NUnit.Framework;

namespace Saltmarsh.P4_2
{
    public sealed class StageContractAcceptanceTests
    {
        [Test]
        public void R2_13_R3_D23_AppOriginCandidateHasSignedClientIntake()
        {
            // Exact public seam requested by R3-C; a ledger-only stage request cannot carry sample bytes.
            bool exists = typeof(CompanionClient).GetMethods().Any(m =>
                string.Equals(m.Name, "StageAppCandidateAsync", StringComparison.Ordinal));
            Assert.That(exists, Is.True,
                "R3-C requires CompanionClient.StageAppCandidateAsync(changeSetId, projectId, sourceRevision, " +
                "catalogRevision, changeSet, toolCatalog, artifactBytes, ct). " +
                "CompanionStageService must route app/sample candidates through signed /v1/stage/app-candidate.");
        }
    }
}
