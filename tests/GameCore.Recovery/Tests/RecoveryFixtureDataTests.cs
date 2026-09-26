// GC-027's committed-data suite: the two fixture documents under `tests/GameCore.Recovery/Data` are read from the
// repository tree, decoded, and checked for the canonical form the rest of the fixture packages commit to.
//
// The form is a contract, not a style: `tests/GameCore.CheckpointFixtures/README.md` records the same rules for the
// GC-018 documents, and a diff that re-indents a fixture would otherwise be indistinguishable from a data change.
// So this suite asserts the encoding (UTF-8 without a BOM, LF only, exactly one trailing newline), the layout (two
// spaces per nesting level, no trailing whitespace, no tab) and the two `format` ids the readers refuse anything
// else in place of.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Recovery.Fixtures;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>The committed fixture documents are canonical UTF-8 text with their documented format ids.</summary>
    [TestFixture]
    public sealed class RecoveryFixtureDataTests
    {
        [Test]
        public void BothCommittedFixturesAreCanonicalUtf8Text()
        {
            AssertCanonical(RecoveryFixturePaths.RecoveryMatrixPath, RecoveryFixturePaths.RecoveryMatrixFile());
            AssertCanonical(RecoveryFixturePaths.StoreVersionsPath, RecoveryFixturePaths.StoreVersionsFile());
        }

        [Test]
        public void BothCommittedFixturesDeclareTheirDocumentedFormat()
        {
            Assert.That(RecoveryFixtureFormat.RecoveryMatrix,
                Is.EqualTo("gamecore.checkpoint-fixtures/recovery-matrix/1"));
            Assert.That(RecoveryFixtureFormat.StoreVersions,
                Is.EqualTo("gamecore.checkpoint-fixtures/store-versions/1"));

            RecoveryMatrixDocument matrix = RecoveryMatrixDocument.ReadFile(RecoveryFixturePaths.RecoveryMatrixFile());
            Assert.That(matrix.Format, Is.EqualTo(RecoveryFixtureFormat.RecoveryMatrix));
            Assert.That(matrix.Format, Is.EqualTo("gamecore.checkpoint-fixtures/recovery-matrix/1"));
            Assert.That(matrix.Cases.Count, Is.GreaterThan(0));

            StoreVersionDocument versions = StoreVersionDocument.ReadFile(RecoveryFixturePaths.StoreVersionsFile());
            Assert.That(versions.Format, Is.EqualTo(RecoveryFixtureFormat.StoreVersions));
            Assert.That(versions.Format, Is.EqualTo("gamecore.checkpoint-fixtures/store-versions/1"));
            Assert.That(versions.Cases.Count, Is.GreaterThan(0));
        }

        [Test]
        public void TheFixtureDocumentsLiveWhereThePackageSaysTheyDo()
        {
            Assert.That(RecoveryFixturePaths.RecoveryMatrixFile(),
                Does.EndWith(Path.Combine("GameCore.Recovery", "Data", "recovery-matrix.json")));
            Assert.That(RecoveryFixturePaths.StoreVersionsFile(),
                Does.EndWith(Path.Combine("GameCore.Recovery", "Data", "checkpoint-store-versions.json")));
            Assert.That(RecoveryFixturePaths.DataDirectory, Is.EqualTo("tests/GameCore.Recovery/Data"));
            Assert.That(RecoveryFixturePaths.PackageDirectory, Is.EqualTo("tests/GameCore.Recovery"));
        }

        private static void AssertCanonical(string relativePath, string absolutePath)
        {
            Assert.That(File.Exists(absolutePath), Is.True, relativePath + " must be committed in the repository tree.");
            byte[] raw = File.ReadAllBytes(absolutePath);
            Assert.That(raw.Length, Is.GreaterThan(2), relativePath + ": a fixture document is more than one line.");

            string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(raw);
            Assert.That(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text), Is.EqualTo(raw),
                relativePath + ": the file is UTF-8 without a byte-order mark, and re-encoding reproduces it exactly.");
            Assert.That(raw[raw.Length - 1], Is.EqualTo((byte)'\n'), relativePath + ": the file ends with one LF.");
            Assert.That(raw[raw.Length - 2], Is.Not.EqualTo((byte)'\n'), relativePath + ": exactly one trailing newline.");
            Assert.That(Array.IndexOf(raw, (byte)'\r'), Is.EqualTo(-1), relativePath + ": LF line endings only.");
            Assert.That(Array.IndexOf(raw, (byte)'\t'), Is.EqualTo(-1), relativePath + ": no tab indentation.");

            string[] lines = text.Split('\n');
            Assert.That(lines[lines.Length - 1], Is.Empty,
                relativePath + ": the trailing newline leaves nothing after it.");

            for (int i = 0; i < lines.Length - 1; i++)
            {
                string line = lines[i];
                string context = relativePath + " line " + (i + 1).ToString(CultureInfo.InvariantCulture);
                Assert.That(line.Length, Is.GreaterThan(0), context + " must not be blank.");
                Assert.That(line, Is.EqualTo(line.TrimEnd(' ', '\t')), context + " has trailing whitespace.");
                Assert.That(Indent(line) % 2, Is.EqualTo(0), context + " is not indented by a multiple of two spaces.");
            }

            AssertIndentMatchesNesting(lines, relativePath);
        }

        /// <summary>
        /// Two spaces per nesting level: every line's indentation is twice the number of brackets still open at that
        /// line, and a line that starts with a closing bracket is dedented with it. This is the canonical form
        /// `tests/GameCore.CheckpointFixtures/README.md` documents, checked from the text rather than asserted.
        /// </summary>
        private static void AssertIndentMatchesNesting(string[] lines, string relativePath)
        {
            int depth = 0;
            for (int i = 0; i < lines.Length - 1; i++)
            {
                string line = lines[i];
                int indent = Indent(line);
                string trimmed = line.Substring(indent);
                string context = relativePath + " line " + (i + 1).ToString(CultureInfo.InvariantCulture);

                int closers = 0;
                while (closers < trimmed.Length && (trimmed[closers] == '}' || trimmed[closers] == ']'))
                {
                    closers++;
                }

                if (closers > 0)
                {
                    string rest = trimmed.Substring(closers).Trim();
                    Assert.That(rest.Length == 0 || rest == ",", Is.True,
                        context + " mixes a closing bracket with content.");
                }

                Assert.That(indent, Is.EqualTo(2 * (depth - closers)),
                    context + ": indentation must be two spaces per nesting level.");

                depth -= closers;
                ScanBrackets(trimmed.Substring(closers), ref depth, context);
                Assert.That(depth, Is.GreaterThanOrEqualTo(0), context + ": a bracket closed that was never opened.");
            }

            Assert.That(depth, Is.EqualTo(0), relativePath + ": the document must close every bracket it opened.");
        }

        /// <summary>Counts the brackets outside string literals, so a bracket inside a statement is not one.</summary>
        private static void ScanBrackets(string text, ref int depth, string context)
        {
            bool inString = false;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                }
            }

            Assert.That(inString, Is.False, context + ": a string literal must close on the line that opens it.");
        }

        private static int Indent(string line)
        {
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
            {
                indent++;
            }

            return indent;
        }
    }
}
