// ToolCatalogBuilder over the annotated sample plugin (AnnotatedSamples.cs) produces Samples/tool-catalog.json.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using GameCore.Studio.Model.Tests.Plugin;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class ToolCatalogBuilderTests
    {
        private static ToolCatalog BuildSample()
        {
            return new ToolCatalogBuilder()
                .AddType(typeof(NpcDefinition))
                .AddType(typeof(DialogueGraph))
                .AddType(typeof(ItemDefinition))
                .AddType(typeof(NpcTools))
                .AddType(typeof(InventoryTools))
                .AddType(typeof(DialogueTools))
                .Build("com.gamecore.gameplay.sample");
        }

        [Test]
        public void SamplePluginProducesTheSampleCatalog()
        {
            ToolCatalog catalog = BuildSample();
            Assert.That(
                JsonEquivalence.Difference(Samples.Token("tool-catalog.json"), StudioJson.ToToken(catalog)),
                Is.Null);
            Assert.That(MiniSchemaValidator.For(typeof(ToolCatalog)).Validate(StudioJson.ToToken(catalog)), Is.Empty);
        }

        [Test]
        public void BuildIsOrderIndependentAndIdempotent()
        {
            ToolCatalog reversed = new ToolCatalogBuilder()
                .AddType(typeof(DialogueTools))
                .AddType(typeof(InventoryTools))
                .AddType(typeof(NpcTools))
                .AddType(typeof(NpcTools))
                .AddType(typeof(ItemDefinition))
                .AddType(typeof(DialogueGraph))
                .AddType(typeof(NpcDefinition))
                .Build("com.gamecore.gameplay.sample");
            Assert.That(StudioJson.Serialize(reversed), Is.EqualTo(StudioJson.Serialize(BuildSample())));
        }

        [Test]
        public void OperationDetailsAreReflected()
        {
            ToolCatalog catalog = BuildSample();
            ToolEntry place = catalog.FindTool("npc.place")!;
            Assert.That(place.Tier, Is.EqualTo(ToolTier.Compose));
            Assert.That(place.TargetType, Is.EqualTo("npc.definition"));
            Assert.That(place.FindArg("yaw")!.Required, Is.False, "an optional C# parameter is not required");
            Assert.That(place.Prerequisites![0].Requires, Is.EqualTo("world.region"));
            Assert.That(place.Prerequisites[0].On, Is.EqualTo(PrerequisiteSubject.Project));
            Assert.That(catalog.FindTool("dialogue.addNode")!.Prerequisites![0].On, Is.EqualTo(PrerequisiteSubject.Target));
            Assert.That(catalog.FindTool("npc.setPatrol")!.Validators![0].Codes, Is.EqualTo(new[] { "InvalidArgs", "ValidationFailed" }));
            Assert.That(catalog.FindObjectType("npc.definition")!.Fields, Has.Count.EqualTo(4), "unannotated members are not exported");
        }

        [Test]
        public void RuntimeApplicabilityIsTheMaximumOfToolAndTargetType()
        {
            ToolCatalog catalog = new ToolCatalogBuilder().AddType(typeof(ItemDefinition)).AddMethod(typeof(ItemTools).GetMethod(nameof(ItemTools.Rename))!).Build();
            Assert.That(catalog.FindTool("item.rename")!.RuntimeApply, Is.EqualTo(RuntimeApply.Rebuild));
        }

        public static class ItemTools
        {
            [AuthorOperation("item.rename")]
            public static void Rename(EditContext ctx, ItemDefinition item, [AuthorArg] string name)
            {
            }
        }

        [Test]
        public void InferredValueTypes()
        {
            ObjectTypeEntry probe = new ToolCatalogBuilder().AddType(typeof(TypeProbe)).Build().FindObjectType("types.probe")!;
            Dictionary<string, FieldSpec> fields = new Dictionary<string, FieldSpec>();
            foreach (FieldSpec field in probe.Fields)
            {
                fields.Add(field.Name, field);
            }

            Assert.That(fields["flag"].Type, Is.EqualTo("bool"));
            Assert.That(fields["big"].Type, Is.EqualTo("int"));
            Assert.That(fields["ratio"].Type, Is.EqualTo("float"));
            Assert.That(fields["label"].Type, Is.EqualTo("string"));
            Assert.That(fields["offset"].Type, Is.EqualTo("vector3"));
            Assert.That(fields["counts"].Type, Is.EqualTo("int[]"));
            Assert.That(fields["crowd"].Type, Is.EqualTo("ref[]"));
            Assert.That(fields["crowd"].Category, Is.EqualTo("npc.definition"));
            Assert.That(fields["opaque"].Type, Is.EqualTo("object"));
            Assert.That(fields["small"].Min, Is.EqualTo(0.1), "a float literal is normalised to its shortest text");
            Assert.That(fields["maybe"].Type, Is.EqualTo("int"));
        }

        [Test]
        public void NumberNormalisation()
        {
            Assert.That(ToolCatalogBuilder.Number(double.NaN), Is.Null);
            Assert.That(ToolCatalogBuilder.Number(0.1f), Is.EqualTo(0.1));
            Assert.That(ToolCatalogBuilder.Number(1.8), Is.EqualTo(1.8));
            Assert.That(ToolCatalogBuilder.Number(6f), Is.EqualTo(6.0));
            Assert.Throws<InvalidOperationException>(() => ToolCatalogBuilder.Number(double.PositiveInfinity));
        }

        [Test]
        public void ScopesExpandInDeclarationOrder()
        {
            Assert.That(ToolCatalogBuilder.ExpandScopes(0), Is.Null);
            Assert.That(ToolCatalogBuilder.ExpandScopes(AuthorScope.Scope | AuthorScope.Instance), Is.EqualTo(new[] { AuthorScope.Instance, AuthorScope.Scope }));
        }

        [Test]
        public void DeclarationErrorsAreRefused()
        {
            Assert.Throws<InvalidOperationException>(() => new ToolCatalogBuilder().AddType(typeof(DuplicateTools)));
            Assert.Throws<InvalidOperationException>(() => new ToolCatalogBuilder().AddType(typeof(BothAttributes)));
            Assert.Throws<InvalidOperationException>(() => new ToolCatalogBuilder().AddType(typeof(BadOverride)));
            Assert.Throws<ArgumentException>(() => new ToolCatalogBuilder().AddMethod(typeof(ToolCatalogBuilderTests).GetMethod(nameof(DeclarationErrorsAreRefused))!));
        }

        [Test]
        public void MergeCombinesCatalogsAndRefusesConflicts()
        {
            ToolCatalog npc = new ToolCatalogBuilder().AddType(typeof(NpcDefinition)).AddType(typeof(NpcTools)).Build("npc");
            ToolCatalog dialogue = new ToolCatalogBuilder().AddType(typeof(DialogueGraph)).AddType(typeof(DialogueTools)).Build("dialogue");
            ToolCatalog merged = ToolCatalog.Merge(new[] { dialogue, npc });
            Assert.That(merged.Plugin, Is.Null);
            Assert.That(merged.Tools, Has.Count.EqualTo(3));
            Assert.That(merged.Tools[0].Id, Is.EqualTo("dialogue.addNode"));
            Assert.That(merged.ObjectTypes, Has.Count.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => ToolCatalog.Merge(new[] { npc, npc }));
        }
    }
}
