// P1.7b dotnet tests: the Structural flag of the mirror attributes against the Studio model, the hardening diagnostic
// codes (well formed, unique, not reused from another code table), the P3.1-agreed action kinds and the explanation /
// migration report shapes. Unity-free.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Logic;
using NUnit.Framework;
using StudioModel = GameCore.Studio.Model;

namespace GameCore.Rules.Gameplay.Tests.Local
{
    [TestFixture]
    public sealed class AuthoringHardeningTests
    {
        private static readonly Regex CodePattern = new Regex("^GP-[A-Z]{3}-[0-9]{3}$", RegexOptions.CultureInvariant);

        [TestCase(typeof(AuthorFieldAttribute), typeof(StudioModel.AuthorFieldAttribute))]
        [TestCase(typeof(AuthorRefAttribute), typeof(StudioModel.AuthorRefAttribute))]
        public void Structural_IsAReadWriteBoolDefaultingToFalse_OnTheMirrorAndTheStudioAttribute(Type mirror, Type studio)
        {
            foreach (Type type in new[] { mirror, studio })
            {
                PropertyInfo? property = type.GetProperty("Structural");
                Assert.That(property, Is.Not.Null, type.FullName);
                Assert.That(property!.PropertyType, Is.EqualTo(typeof(bool)), type.FullName);
                Assert.That(property.CanRead && property.CanWrite, Is.True, type.FullName);
                Assert.That(property.GetValue(Activator.CreateInstance(type)), Is.EqualTo(false), type.FullName);
            }
        }

        [Test]
        public void ReadOnly_IsAReadWriteBoolDefaultingToFalse_OnTheMirrorAndTheStudioOperationAttribute()
        {
            foreach (object operation in new object[] { new AuthorOperationAttribute("probe.tool"), new StudioModel.AuthorOperationAttribute("probe.tool") })
            {
                PropertyInfo? property = operation.GetType().GetProperty("ReadOnly");
                Assert.That(property, Is.Not.Null, operation.GetType().FullName);
                Assert.That(property!.PropertyType, Is.EqualTo(typeof(bool)), operation.GetType().FullName);
                Assert.That(property.CanRead && property.CanWrite, Is.True, operation.GetType().FullName);
                Assert.That(property.GetValue(operation), Is.EqualTo(false), operation.GetType().FullName);
            }
        }

        [Test]
        public void HardeningCodes_AreWellFormedUniqueAndListed()
        {
            List<string> declared = Constants(typeof(AuthoringHardeningCodes)).Select(c => c.Value).ToList();
            Assert.That(declared, Is.Not.Empty);
            Assert.That(declared, Is.All.Matches<string>(code => CodePattern.IsMatch(code)));
            Assert.That(declared.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(declared.Count), "no code is declared twice");
            Assert.That(AuthoringHardeningCodes.All, Is.EquivalentTo(declared), "All lists every declared code");
        }

        [Test]
        public void HardeningCodes_ReuseNoCodeOfAnotherTable()
        {
            var hardening = new HashSet<string>(Constants(typeof(AuthoringHardeningCodes)).Select(c => c.Value), StringComparer.Ordinal);
            IEnumerable<(string Owner, string Value)> others = typeof(AuthoringHardeningCodes).Assembly.GetTypes()
                .Where(t => t != typeof(AuthoringHardeningCodes))
                .SelectMany(t => Constants(t).Select(c => (Owner: t.FullName + "." + c.Name, c.Value)))
                .Where(c => CodePattern.IsMatch(c.Value));
            foreach ((string owner, string value) in others)
            {
                Assert.That(hardening.Contains(value), Is.False, owner + " already uses " + value);
            }
        }

        [Test]
        public void AgreedActionKinds_HaveTheirValuesAndDescriptions()
        {
            Assert.That((int)ActionKind.RestoreStamina, Is.EqualTo(17));
            Assert.That((int)ActionKind.Buy, Is.EqualTo(18));
            Assert.That(new ActionModel(ActionKind.RestoreStamina, 0, 0, 25, string.Empty, string.Empty, "stamina").Describe(), Is.EqualTo("restore 25 stamina"));
            Assert.That(new ActionModel(ActionKind.Buy, 7, 9, 2, string.Empty, string.Empty, "Lantern").Describe(), Is.EqualTo("buy 2 x Lantern"));
        }

        [Test]
        public void ConditionExplanation_NamesTheFailedCondition()
        {
            var failed = new ConditionExplanation("narrative.fact.gate_open", "narrative.fact.gate_open", true, false, 0, "fact gate_open != 0 (read 0)", new[] { "fact gate_open = 0" });
            Assert.That(failed.ToString(), Is.EqualTo("narrative.fact.gate_open: fails at #0 fact gate_open != 0 (read 0)"));
            var unknown = new ConditionExplanation("x", string.Empty, false, false, -1, "no set", null!);
            Assert.That(unknown.Inputs, Is.Empty);
            Assert.That(unknown.ToString(), Does.Contain("unknown condition"));
        }

        [Test]
        public void MigrationReport_CollectsChangedAndUnresolvedLines()
        {
            var report = new AuthoringMigrationReport("npc.dialogueGraph");
            report.AddChanged("a.asset: dialogueGraph 'dialogue.maren' -> dialogue b.asset");
            report.AddUnresolved("c.asset: dialogueGraph 'nobody' names no dialogue graph");
            Assert.That(report.MigrationId, Is.EqualTo("npc.dialogueGraph"));
            Assert.That(report.Changed, Has.Count.EqualTo(1));
            Assert.That(report.Unresolved, Has.Count.EqualTo(1));
        }

        [Test]
        public void AuthorRefCategories_AreTheAgreedIds()
        {
            Assert.That(AuthorRefCategories.EntityInstance, Is.EqualTo("entity.instance"));
            Assert.That(AuthorRefCategories.AssetScene, Is.EqualTo("asset.scene"));
            Assert.That(AuthorRefCategories.AudioClip, Is.EqualTo("audio.clip"));
        }

        private static IEnumerable<(string Name, string Value)> Constants(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => (f.Name, (string)f.GetRawConstantValue()!));
    }
}
