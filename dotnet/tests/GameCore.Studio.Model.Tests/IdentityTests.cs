// Identity derivation (02 s6): TargetId = StableNameKeyDerivation.Derive("auth." + authoringId), ULID-style change-set/selection ids, content stamps.
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;
using GameCore.Studio.Model;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    public sealed class IdentityTests
    {
        private sealed class FixedEntropy : IIdEntropy
        {
            private readonly byte[] _bytes;

            public FixedEntropy(byte[] bytes)
            {
                _bytes = bytes;
            }

            public void Fill(byte[] buffer)
            {
                Array.Copy(_bytes, buffer, buffer.Length);
            }
        }

        [Test]
        public void TargetIdIsTheKernelDerivationOfAuthPrefixedId()
        {
            const string authoringId = "7f1c2a9e-4b3d-4e8f-9a1b-2c3d4e5f6a7b";
            TargetId target = IdDerivation.TargetIdFor(authoringId);
            Assert.That(target.Value, Is.EqualTo(StableNameKeyDerivation.Derive("auth." + authoringId)));
            Assert.That(IdDerivation.AuthoringNamePrefix, Is.EqualTo("auth."));
            Assert.That(target.IsDefault, Is.False);
            Assert.That(IdDerivation.TargetIdFor(authoringId), Is.EqualTo(target), "stable across calls");
            Assert.That(IdDerivation.TargetIdFor("7f1c2a9e4b3d4e8f9a1b2c3d4e5f6a7b"), Is.Not.EqualTo(target));
            foreach (string id in new[] { "a", "0123abcd", "x_y-z.0" })
            {
                Assert.That(IdDerivation.TargetIdFor(id).Value, Is.EqualTo(StableNameKeyDerivation.Derive("auth." + id)), id);
            }
        }

        [Test]
        public void TargetIdRejectsNonCanonicalAuthoringIds()
        {
            Assert.Throws<ArgumentNullException>(() => IdDerivation.TargetIdFor(null!));
            Assert.Throws<ArgumentException>(() => IdDerivation.TargetIdFor(""));
            Assert.Throws<ArgumentException>(() => IdDerivation.TargetIdFor("7F1C2A9E"));
            Assert.Throws<ArgumentException>(() => IdDerivation.TargetIdFor("{7f1c2a9e}"));
            Assert.Throws<ArgumentException>(() => IdDerivation.TargetIdFor(".7f1c"));
            Assert.Throws<ArgumentException>(() => IdDerivation.TargetIdFor("auth:7f1c"));
        }

        [Test]
        public void UlidMatchesTheReferenceEncoding()
        {
            // ULID specification example: time 1469918176385 encodes to "01ARYZ6S41".
            Assert.That(IdDerivation.FormatUlid(1469918176385L, new byte[10]).Substring(0, 10), Is.EqualTo("01ARYZ6S41"));
            Assert.That(IdDerivation.FormatUlid(0, new byte[10]), Is.EqualTo(new string('0', 26)));
            byte[] ones = new byte[10];
            for (int i = 0; i < ones.Length; i++)
            {
                ones[i] = 0xFF;
            }

            Assert.That(IdDerivation.FormatUlid(IdDerivation.MaxUlidTime, ones), Is.EqualTo("7" + new string('Z', 25)));
        }

        [Test]
        public void ChangeSetIdIsDeterministicUnderAFixedClockAndEntropy()
        {
            // Cross-checked with an independent Python encoder: time 1759561200000, entropy sha256("cs")[0..10].
            byte[] entropy = new byte[10];
            Array.Copy(SHA256.HashData(Encoding.UTF8.GetBytes("cs")), entropy, 10);
            string id = IdDerivation.NewChangeSetId(1759561200000L, new FixedEntropy(entropy));
            Assert.That(id, Is.EqualTo("cs_01K6Q0ACC07E5S3HTP4YZEASPW"));
            Assert.That(IdDerivation.IsChangeSetId(id), Is.True);
            Assert.That(IdDerivation.NewChangeSetId(1759561200000L, new FixedEntropy(entropy)), Is.EqualTo(id));

            string later = IdDerivation.NewChangeSetId(1759561200001L, new FixedEntropy(new byte[10]));
            Assert.That(string.CompareOrdinal(later, id), Is.GreaterThan(0), "ids sort by time");
            Assert.That(IdDerivation.IsSelectionId(IdDerivation.NewSelectionId(1759561190000L, new FixedEntropy(entropy))), Is.True);
            Assert.That(IdDerivation.IsChangeSetId(IdDerivation.NewChangeSetId()), Is.True);
        }

        [Test]
        public void ChangeSetIdPatternRejectsMalformedIds()
        {
            Assert.That(IdDerivation.IsChangeSetId("cs_01J"), Is.False);
            Assert.That(IdDerivation.IsChangeSetId("cs_01K6Q0ACC07E5S3HTP4YZEASPU"), Is.False, "U is not Crockford");
            Assert.That(IdDerivation.IsChangeSetId("cs_81K6Q0ACC07E5S3HTP4YZEASPW"), Is.False, "first digit above 7 overflows 128 bits");
            Assert.That(IdDerivation.IsChangeSetId("sel_01K6Q0ACC07E5S3HTP4YZEASPW"), Is.False);
            Assert.That(IdDerivation.IsChangeSetId(null), Is.False);
        }

        [Test]
        public void UlidRejectsOutOfRangeInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => IdDerivation.FormatUlid(-1, new byte[10]));
            Assert.Throws<ArgumentOutOfRangeException>(() => IdDerivation.FormatUlid(IdDerivation.MaxUlidTime + 1, new byte[10]));
            Assert.Throws<ArgumentException>(() => IdDerivation.FormatUlid(0, new byte[9]));
        }

        [Test]
        public void ContentStampIsSha256OverTheBytes()
        {
            Assert.That(ContentStamp.OfUtf8("abc"), Is.EqualTo("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(ContentStamp.Of(new byte[0]), Is.EqualTo("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
            Assert.That(ContentStamp.Sha256Hex(Encoding.UTF8.GetBytes("abc")), Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(ContentStamp.IsValid(ContentStamp.OfUtf8("x")), Is.True);
            Assert.That(ContentStamp.IsValid("sha256:BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"), Is.False);
            Assert.That(ContentStamp.IsValid("md5:abc"), Is.False);
            Assert.That(ContentStamp.DigestOf(ContentStamp.OfUtf8("abc")), Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.Throws<ArgumentException>(() => ContentStamp.DigestOf("sha256:zz"));
        }
    }
}
