// P1.1 dotnet tests: gameplay.contracts against the Studio model (identity and attribute parity), units, payloads.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using NUnit.Framework;
using Studio = GameCore.Studio.Model;

namespace GameCore.Rules.Gameplay.Tests.Local
{
    [TestFixture]
    public sealed class GameplayContractsTests
    {
        private static readonly string[] SampleIds =
        {
            "00000000-0000-0000-0000-000000000001",
            "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
            "a1b2c3d4-e5f6-4789-abcd-ef0123456789",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
        };

        [Test]
        public void TargetIdFor_EqualsTheStudioModelDerivation()
        {
            foreach (string id in SampleIds)
            {
                Assert.That(AuthoringIds.IsValid(id), Is.True, id);
                Assert.That(AuthoringIds.TargetIdFor(id), Is.EqualTo(Studio.IdDerivation.TargetIdFor(id)), id);
                Assert.That(AuthoringIds.TargetIdFor(id), Is.EqualTo(new TargetId(StableNameKeyDerivation.Derive("auth." + id))));
            }

            for (int i = 0; i < 64; i++)
            {
                string minted = AuthoringIds.Mint();
                Assert.That(AuthoringIds.IsValid(minted), Is.True, minted);
                Assert.That(AuthoringIds.TargetIdFor(minted), Is.EqualTo(Studio.IdDerivation.TargetIdFor(minted)));
            }
        }

        [Test]
        public void AuthoringIds_AcceptOnlyCanonicalNonZeroLowercaseGuids()
        {
            Assert.That(AuthoringIds.IsValid(null), Is.False);
            Assert.That(AuthoringIds.IsValid(string.Empty), Is.False);
            Assert.That(AuthoringIds.IsValid("00000000-0000-0000-0000-000000000000"), Is.False, "the zero GUID is not an id");
            Assert.That(AuthoringIds.IsValid("3F2504E0-4F89-11D3-9A0C-0305E82C3301"), Is.False, "uppercase is not canonical");
            Assert.That(AuthoringIds.IsValid("3f2504e04f8911d39a0c0305e82c3301"), Is.False, "the D format is required");
            Assert.That(AuthoringIds.IsValid("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}"), Is.False);
            Assert.Throws<ArgumentException>(() => AuthoringIds.TargetIdFor("Not An Id"));
        }

        [Test]
        public void StableKeysAndRevisions_AreDeterministicAndNeverZero()
        {
            var keys = new HashSet<int>();
            foreach (string id in SampleIds)
            {
                int key = AuthoringIds.StableKey(id);
                Assert.That(key, Is.GreaterThan(0));
                Assert.That(AuthoringIds.StableKey(id), Is.EqualTo(key));
                keys.Add(key);
            }

            Assert.That(keys.Count, Is.EqualTo(SampleIds.Length));
            Assert.That(AuthoringIds.RevisionOfContentStamp(new string('0', 64)), Is.EqualTo(1UL), "revision zero is reserved");
            Assert.That(AuthoringIds.RevisionOfContentStamp("00000000000000ff" + new string('a', 48)), Is.EqualTo(255UL));
            Assert.That(AuthoringIds.ScopeIdFor(SampleIds[1]), Is.Not.EqualTo(AuthoringIds.ScopeIdFor(SampleIds[2])));
        }

        [Test]
        public void SlotNames_AreKebabCasedAndDistinct()
        {
            Assert.That(SlotNames.StableName("entity", "scaleMilli"), Is.EqualTo("slot.entity.scale-milli"));
            Assert.That(SlotNames.StableName("world", "posX"), Is.EqualTo("slot.world.pos-x"));
            var slots = new[]
            {
                GameplaySlots.Alive, GameplaySlots.Variant, GameplaySlots.ScaleMilli, GameplaySlots.Visible,
                GameplaySlots.Residency, GameplaySlots.Visits, GameplaySlots.Region, GameplaySlots.PosX,
                GameplaySlots.PosY, GameplaySlots.PosZ, GameplaySlots.Yaw,
            };
            Assert.That(slots.Distinct().Count(), Is.EqualTo(slots.Length));
            Assert.That(GameplaySlots.TryEntitySlot("scaleMilli", out SlotId scale) && scale.Equals(GameplaySlots.ScaleMilli), Is.True);
        }

        [Test]
        public void IdRegistry_DetectsCollisions()
        {
            var registry = new GameplayIdRegistry();
            RouteId first = registry.Route("entities.route.spawn");
            Assert.That(registry.Route("entities.route.spawn"), Is.EqualTo(first), "re-registering the same name is idempotent");
            Assert.Throws<InvalidOperationException>(() => registry.Stage("entities.route.spawn"), "one identity, two meanings");
        }

        [Test]
        public void Units_RoundHalfAwayFromZero_AndNormalizeYaw()
        {
            Assert.That(GameplayUnits.ToMillimetres(1.0625), Is.EqualTo(1063), "exact binary half rounds away from zero");
            Assert.That(GameplayUnits.ToMillimetres(-1.0625), Is.EqualTo(-1063));
            Assert.That(GameplayUnits.ToMillimetres(2.5), Is.EqualTo(2500));
            Assert.That(GameplayUnits.DegreesToMilliradians(180.0), Is.EqualTo(3142));
            int yaw = GameplayUnits.NormalizeYaw(GameplayUnits.MilliradiansPerTurn + 10);
            Assert.That(yaw, Is.EqualTo(10));
            Assert.That(GameplayUnits.NormalizeYaw(-10), Is.EqualTo(GameplayUnits.MilliradiansPerTurn - 10));
        }

        [Test]
        public void Payloads_RoundTripLittleEndian()
        {
            var writer = new GameplayPayloadWriter();
            writer.Int32(-5).UInt64(0x0102030405060708UL).Id(StableNameKeyDerivation.Derive("x"));
            byte[] bytes = writer.ToArray();
            Assert.That(bytes.Length, Is.EqualTo(4 + 8 + 16));
            Assert.That(bytes[4], Is.EqualTo(0x08), "little-endian");
            var reader = new GameplayPayloadReader(bytes);
            Assert.That(reader.Int32(), Is.EqualTo(-5));
            Assert.That(reader.UInt64(), Is.EqualTo(0x0102030405060708UL));
            Assert.That(reader.Id(), Is.EqualTo(StableNameKeyDerivation.Derive("x")));
        }

        [Test]
        public void DiagnosticCodes_AreUniqueAndPrefixed()
        {
            Assert.That(GameplayDiagnosticCodes.All.Distinct().Count(), Is.EqualTo(GameplayDiagnosticCodes.All.Count));
            Assert.That(GameplayDiagnosticCodes.All.All(c => c.StartsWith("GP-", StringComparison.Ordinal)), Is.True);
        }

        // ------------------------------------------------------------------ mirror attributes (no studio dependency)

        [TestCase(typeof(AuthorableAttribute), typeof(Studio.AuthorableAttribute))]
        [TestCase(typeof(AuthorFieldAttribute), typeof(Studio.AuthorFieldAttribute))]
        [TestCase(typeof(AuthorRefAttribute), typeof(Studio.AuthorRefAttribute))]
        [TestCase(typeof(AuthorOperationAttribute), typeof(Studio.AuthorOperationAttribute))]
        [TestCase(typeof(AuthorArgAttribute), typeof(Studio.AuthorArgAttribute))]
        [TestCase(typeof(AuthorValidatorAttribute), typeof(Studio.AuthorValidatorAttribute))]
        public void MirrorAttribute_HasTheStudioShape(Type mirror, Type studio)
        {
            Assert.That(mirror.Name, Is.EqualTo(studio.Name));
            AttributeUsageAttribute mine = mirror.GetCustomAttribute<AttributeUsageAttribute>()!;
            AttributeUsageAttribute theirs = studio.GetCustomAttribute<AttributeUsageAttribute>()!;
            Assert.That(mine.ValidOn, Is.EqualTo(theirs.ValidOn));
            Assert.That(mine.AllowMultiple, Is.EqualTo(theirs.AllowMultiple));
            Assert.That(mine.Inherited, Is.EqualTo(theirs.Inherited));
            Assert.That(Shape(mirror), Is.EqualTo(Shape(studio)), "same public properties, types and constructor");

            object? a = Construct(mirror);
            object? b = Construct(studio);
            foreach (PropertyInfo property in studio.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.DeclaringType == studio))
            {
                object? mineDefault = mirror.GetProperty(property.Name)!.GetValue(a);
                object? theirDefault = property.GetValue(b);
                Assert.That(Text(mineDefault), Is.EqualTo(Text(theirDefault)), property.Name + " default");
            }
        }

        [TestCase(typeof(AuthoringKind), typeof(Studio.AuthoringKind))]
        [TestCase(typeof(AuthorScope), typeof(Studio.AuthorScope))]
        [TestCase(typeof(ToolTier), typeof(Studio.ToolTier))]
        [TestCase(typeof(RuntimeApply), typeof(Studio.RuntimeApply))]
        public void MirrorEnum_HasTheStudioMembers(Type mirror, Type studio)
        {
            Assert.That(mirror.Name, Is.EqualTo(studio.Name));
            Assert.That(mirror.IsDefined(typeof(FlagsAttribute)), Is.EqualTo(studio.IsDefined(typeof(FlagsAttribute))));
            Assert.That(Members(mirror), Is.EqualTo(Members(studio)));
        }

        private static string Shape(Type type)
        {
            IEnumerable<string> properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.DeclaringType == type)
                .Select(p => p.Name + ":" + TypeName(p.PropertyType) + (p.CanWrite ? "/rw" : "/r"))
                .OrderBy(s => s, StringComparer.Ordinal);
            IEnumerable<string> constructors = type.GetConstructors()
                .Select(c => "ctor(" + string.Join(",", c.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ")")
                .OrderBy(s => s, StringComparer.Ordinal);
            return string.Join(";", properties.Concat(constructors));
        }

        private static string TypeName(Type type)
        {
            if (type.IsArray)
            {
                return TypeName(type.GetElementType()!) + "[]";
            }

            Type? nullable = Nullable.GetUnderlyingType(type);
            return nullable != null ? TypeName(nullable) + "?" : type.Name;
        }

        private static string Members(Type enumType) =>
            string.Join(",", Enum.GetNames(enumType).Select(n => n + "=" + Convert.ToInt64(Enum.Parse(enumType, n)).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        private static object? Construct(Type type)
        {
            ConstructorInfo ctor = type.GetConstructors()[0];
            object?[] args = ctor.GetParameters().Select(p => (object?)"x").ToArray();
            return ctor.Invoke(args);
        }

        private static string Text(object? value) => value == null ? "null" : value is Enum e ? Convert.ToInt64(e).ToString(System.Globalization.CultureInfo.InvariantCulture) : value.ToString() ?? "null";
    }
}
