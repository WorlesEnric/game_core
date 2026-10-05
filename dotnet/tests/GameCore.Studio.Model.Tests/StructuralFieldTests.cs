// P1.7b (B4): the Structural flag of [AuthorField]/[AuthorRef] reaches FieldSpec.structural, is written only when true
// (so catalogs without structural fields keep their canonical text and revision) and conforms to the catalog schema.
#nullable enable
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class StructuralFieldTests
    {
        [Authorable("sample.structural", DisplayName = "Structural sample")]
        public sealed class StructuralSample
        {
            [AuthorRef(Category = "asset.prefab", Structural = true, Doc = "Shapes the build.")]
            public Plugin.PrefabAsset? prefab;

            [AuthorField(Structural = true, Doc = "Slot layout.")]
            public int slots = 4;

            [AuthorField(Doc = "Tuning.")]
            public float speed = 1f;

            [AuthorRef(Category = "asset.prefab", Required = false, Doc = "Presentation only.")]
            public Plugin.PrefabAsset? icon;
        }

        [Test]
        public void AttributesDefaultToNotStructural()
        {
            Assert.That(new AuthorFieldAttribute().Structural, Is.False);
            Assert.That(new AuthorRefAttribute().Structural, Is.False);
        }

        [Test]
        public void StructuralReachesTheFieldSpecAndOnlyTrueIsWritten()
        {
            ToolCatalog catalog = new ToolCatalogBuilder().AddType(typeof(StructuralSample)).Build("p");
            ObjectTypeEntry type = catalog.ObjectTypes.Single(t => t.TypeId == "sample.structural");
            Assert.That(type.Fields.Single(f => f.Name == "prefab").Structural, Is.True);
            Assert.That(type.Fields.Single(f => f.Name == "slots").Structural, Is.True);
            Assert.That(type.Fields.Single(f => f.Name == "speed").Structural, Is.Null, "a tuning field omits the member");
            Assert.That(type.Fields.Single(f => f.Name == "icon").Structural, Is.Null);

            JToken token = StudioJson.ToToken(catalog);
            JArray fields = (JArray)token["objectTypes"]![0]!["fields"]!;
            Assert.That(fields.Count(f => f["structural"] != null), Is.EqualTo(2));
            Assert.That(fields.Where(f => f["structural"] != null).All(f => f["structural"]!.Value<bool>()), Is.True);
            Assert.That(MiniSchemaValidator.For(typeof(ToolCatalog)).Validate(token), Is.Empty);

            ToolCatalog back = StudioJson.Deserialize<ToolCatalog>(token.ToString());
            Assert.That(back.ObjectTypes[0].Fields.Count(f => f.Structural == true), Is.EqualTo(2));
            Assert.That(back.WithRevision().Revision, Is.EqualTo(catalog.WithRevision().Revision));
        }

        [Test]
        public void FalseIsNotWrittenSoOldCatalogsKeepTheirRevision()
        {
            var plain = new FieldSpec("speed", "float", true);
            var explicitFalse = new FieldSpec("speed", "float", true, structural: false);
            Assert.That(explicitFalse.Structural, Is.Null);
            Assert.That(StudioJson.Canonical(StudioJson.ToToken(explicitFalse)), Is.EqualTo(StudioJson.Canonical(StudioJson.ToToken(plain))));
            Assert.That(StudioJson.Canonical(StudioJson.ToToken(new FieldSpec("prefab", "ref", true, structural: true))), Does.Contain("\"structural\":true"));
        }
    }
}
