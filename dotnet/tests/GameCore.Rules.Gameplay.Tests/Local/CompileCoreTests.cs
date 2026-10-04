// P1.1 dotnet tests: the Unity-free compile core - definition hashing, bake validation, report determinism and the
// catalog description, validated by the real content compiler.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Content.Compiler;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Local
{
    [TestFixture]
    public sealed class CompileCoreTests
    {
        private const string World = "10000000-0000-4000-8000-000000000001";
        private const string Village = "20000000-0000-4000-8000-000000000001";
        private const string Marsh = "20000000-0000-4000-8000-000000000002";
        private const string Belfry = "20000000-0000-4000-8000-000000000003";
        private const string PortalVm = "30000000-0000-4000-8000-000000000001";
        private const string PortalMb = "30000000-0000-4000-8000-000000000002";
        private const string PortalBv = "30000000-0000-4000-8000-000000000003";
        private const string Crate = "40000000-0000-4000-8000-000000000001";
        private const string Stone = "40000000-0000-4000-8000-000000000002";
        private const string Traveller = "50000000-0000-4000-8000-000000000001";
        private const string CrateA = "50000000-0000-4000-8000-000000000002";
        private const string StoneA = "50000000-0000-4000-8000-000000000003";

        [Test]
        public void DefinitionHash_CoversValuesNotOrder_AndOneFieldChangesOnlyItsRevision()
        {
            CanonicalFields a = new CanonicalFields("entity.definition").Add("defaultScaleMilli", 1000).Add("startsVisible", true).Add("prefab", "asset:abc:1");
            CanonicalFields b = new CanonicalFields("entity.definition").Add("prefab", "asset:abc:1").Add("startsVisible", true).Add("defaultScaleMilli", 1000);
            Assert.That(DefinitionHashing.HashHex(a), Is.EqualTo(DefinitionHashing.HashHex(b)), "field order never matters");
            Assert.That(DefinitionHashing.HashHex(a), Has.Length.EqualTo(64));

            CanonicalFields other = new CanonicalFields("entity.definition").Add("defaultScaleMilli", 1000).Add("startsVisible", true).Add("prefab", "asset:def:1");
            string unchangedOther = DefinitionHashing.HashHex(other);
            CanonicalFields edited = new CanonicalFields("entity.definition").Add("defaultScaleMilli", 1001).Add("startsVisible", true).Add("prefab", "asset:abc:1");
            Assert.That(DefinitionHashing.RevisionOf(DefinitionHashing.HashHex(edited)), Is.Not.EqualTo(DefinitionHashing.RevisionOf(DefinitionHashing.HashHex(a))));
            Assert.That(DefinitionHashing.HashHex(other), Is.EqualTo(unchangedOther), "another definition keeps its hash");
            Assert.That(CanonicalValues.Escape("a=b\nc"), Is.EqualTo("a\\eb\\nc"), "values cannot forge field lines");
            Assert.That(CanonicalValues.Float(0.1f), Is.EqualTo(CanonicalValues.Float(0.1f)));
            Assert.Throws<InvalidOperationException>(() => new CanonicalFields("t").Add("x", 1).Add("x", 2));
        }

        [Test]
        public void ValidWorld_PassesTheValidator()
        {
            Assert.That(BakeValidator.Validate(SampleWorld()), Is.Empty);
        }

        [Test]
        public void Validator_ReportsDuplicateIdsSelfLoopsMissingStartAndBadEntities()
        {
            BakedWorld world = SampleWorld();
            world.Entities[1].AuthoringId = world.Entities[0].AuthoringId;
            world.Portals[0].RegionB = world.Portals[0].RegionA;
            world.StartRegionId = string.Empty;
            world.Entities[2].Variant = 7;
            world.Entities[2].Overrides["speed"] = "3";
            world.Regions[2].ExtentX = 0;
            var codes = new HashSet<string>();
            foreach (GameplayDiagnostic diagnostic in BakeValidator.Validate(world))
            {
                codes.Add(diagnostic.Code);
            }

            Assert.That(codes, Does.Contain(GameplayDiagnosticCodes.DuplicateAuthoringId));
            Assert.That(codes, Does.Contain(GameplayDiagnosticCodes.PortalTargetsOwnRegion));
            Assert.That(codes, Does.Contain(GameplayDiagnosticCodes.WorldMissingStartRegion));
            Assert.That(codes, Does.Contain(GameplayDiagnosticCodes.EntityVariantOutOfRange));
            Assert.That(codes, Does.Contain(GameplayDiagnosticCodes.RegionMissingBounds));
        }

        [Test]
        public void BakeReport_IsDeterministic_AndIndependentOfAuthoringOrder()
        {
            BakedWorld forward = SampleWorld();
            BakedWorld shuffled = SampleWorld();
            shuffled.Entities.Reverse();
            shuffled.Regions.Reverse();
            shuffled.Portals.Reverse();
            shuffled.Definitions.Reverse();
            forward.Canonicalize();
            shuffled.Canonicalize();
            string a = BakeReportWriter.Write(forward, "fingerprint");
            string b = BakeReportWriter.Write(shuffled, "fingerprint");
            Assert.That(b, Is.EqualTo(a));
            Assert.That(BakeReportWriter.Write(forward, "fingerprint"), Is.EqualTo(a), "two bakes are byte-identical");
            Assert.That(a, Does.Not.Contain("\r"), "LF only");
            Assert.That(a.EndsWith("\n", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void CatalogDescription_ValidatesThroughTheContentCompiler_AndIsDeterministic()
        {
            BakedWorld world = SampleWorld();
            world.Canonicalize();
            var naming = new CatalogNaming("Hollowmere.Generated", "HollowmereCatalog");
            string description = CatalogDescriptionWriter.Write(world, naming);
            Assert.That(CatalogDescriptionWriter.Write(world, naming), Is.EqualTo(description));

            CatalogCompilationResult compiled = CatalogDescriptionReader.Read(description);
            Assert.That(compiled.Succeeded, Is.True, compiled.Describe());
            string code = compiled.GeneratedCode!;
            Assert.That(code, Does.Contain("public static class HollowmereCatalog"));
            Assert.That(code, Does.Contain(CatalogDescriptionWriter.RecipeKeyName(Crate)));
            Assert.That(CatalogGenerator.ExtractStringConstant(code, "CatalogFingerprint"), Has.Length.EqualTo(64));

            // A definition revision change changes that recipe's implementation id and the fingerprint, nothing else.
            BakedWorld edited = SampleWorld();
            edited.Definitions[0].ContentHash = DefinitionHashing.Sha256Hex("edited");
            edited.Canonicalize();
            CatalogCompilationResult recompiled = CatalogDescriptionReader.Read(CatalogDescriptionWriter.Write(edited, naming));
            Assert.That(recompiled.Succeeded, Is.True, recompiled.Describe());
            Assert.That(CatalogGenerator.ExtractStringConstant(recompiled.GeneratedCode!, "CatalogFingerprint"),
                Is.Not.EqualTo(CatalogGenerator.ExtractStringConstant(code, "CatalogFingerprint")));

            // The static-only description (no definitions) is a valid catalog too.
            var empty = new BakedWorld { WorldId = World, WorldName = "Empty" };
            CatalogCompilationResult staticOnly = CatalogDescriptionReader.Read(CatalogDescriptionWriter.Write(empty, naming));
            Assert.That(staticOnly.Succeeded, Is.True, staticOnly.Describe());
        }

        [Test]
        public void RecipeKeys_MatchTheRuntimeLookupKeys()
        {
            Assert.That(CatalogDescriptionWriter.RecipeName(Crate), Is.EqualTo(GameplayCatalogNames.DefinitionRecipePrefix + Crate));
            Assert.That(GameplayIds.StableName(CatalogDescriptionWriter.RecipeName(Crate)), Is.EqualTo("gameplay.recipe." + Crate));
        }

        internal static BakedWorld SampleWorld()
        {
            var world = new BakedWorld
            {
                WorldId = World,
                WorldName = "Sample",
                StartRegionId = Village,
                FocusEntityId = Traveller,
            };
            world.Regions.Add(Region(Village, "Village", 0));
            world.Regions.Add(Region(Marsh, "Marsh", 200000));
            world.Regions.Add(Region(Belfry, "Belfry", 400000));
            world.Portals.Add(Portal(PortalVm, Village, Marsh));
            world.Portals.Add(Portal(PortalMb, Marsh, Belfry));
            world.Portals.Add(Portal(PortalBv, Belfry, Village));
            world.Definitions.Add(Definition(Crate, "Crate", 2));
            world.Definitions.Add(Definition(Stone, "Stone", 1));
            world.Entities.Add(Entity(Traveller, "Traveller", Village, Stone, 0));
            world.Entities.Add(Entity(CrateA, "Crate A", Village, Crate, 1));
            world.Entities.Add(Entity(StoneA, "Stone A", Marsh, Stone, 0));
            return world;
        }

        private static BakedRegion Region(string id, string name, int x) => new BakedRegion
        {
            AuthoringId = id,
            Name = name,
            ScenePath = "Assets/Regions/" + name + ".unity",
            BoundsX = x,
            ExtentX = 40000,
            ExtentY = 15000,
            ExtentZ = 40000,
        };

        private static BakedPortal Portal(string id, string a, string b) => new BakedPortal
        {
            AuthoringId = id,
            Name = "portal",
            RegionA = a,
            RegionB = b,
            HasEndA = true,
            HasEndB = true,
            ArrivalA = new BakedPose(1000, 0, 0, 0),
            ArrivalB = new BakedPose(-1000, 0, 0, 3142),
        };

        private static BakedDefinition Definition(string id, string name, int variants)
        {
            var definition = new BakedDefinition
            {
                AuthoringId = id,
                Name = name,
                ContentHash = DefinitionHashing.Sha256Hex(name),
                VariantCount = variants,
            };
            definition.OverridableFields.Add("scaleMilli");
            return definition;
        }

        private static BakedEntity Entity(string id, string name, string region, string definition, int variant) => new BakedEntity
        {
            AuthoringId = id,
            Name = name,
            RegionId = region,
            DefinitionId = definition,
            Variant = variant,
        };
    }
}
