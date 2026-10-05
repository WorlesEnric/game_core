#nullable enable
using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace GameCore.Stage.Analysis.Tests
{
    public sealed class ContractTests
    {
        [Test]
        public void R2_11_StageIntSharedWireFixture()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "samples"))) directory = directory.Parent;
            Assert.That(directory, Is.Not.Null);
            var fixture = Path.Combine(directory!.FullName, "studio/stage/analyzer/Tests/Fixtures");
            var request = File.ReadAllText(Path.Combine(fixture, "request.json"));
            Assert.That(Rules.Parse(request).Policy.Mode, Is.EqualTo("D1"));
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                File.WriteAllText(Path.Combine(temp, "package.json"), "{\"name\":\"com.example.fixture\"}");
                File.WriteAllText(Path.Combine(temp, "Good.cs"), "#nullable enable\nclass Good {}\n");
                File.WriteAllText(Path.Combine(temp, "request.json"), request);
                var output = Path.Combine(temp, "result.json");
                Assert.That(Program.Main(new[] { "--root", temp, "--rules", Path.Combine(temp, "request.json"), "--out", output }), Is.Zero);
                // The Rust regression consumes the exact bytes emitted by the CLI here.
                var bytes = File.ReadAllText(output);
                Assert.That(bytes, Is.EqualTo(File.ReadAllText(Path.Combine(fixture, "result.json"))));
                var destination = Environment.GetEnvironmentVariable("STAGE_CONTRACT_FIXTURE_OUT");
                if (destination != null) File.WriteAllText(destination, bytes);
                File.WriteAllText(Path.Combine(temp, "Good.cs"), "#nullable enable\nclass Bad { void Run() { System.Diagnostics.Process.Start(\"x\"); } }\n");
                Assert.That(Program.Main(new[] { "--root", temp, "--rules", Path.Combine(temp, "request.json"), "--out", output }), Is.EqualTo(3));
                using var result = JsonDocument.Parse(File.ReadAllText(output));
                Assert.That(result.RootElement.GetProperty("pass").GetBoolean(), Is.False);
                var hit = result.RootElement.GetProperty("findings")[0];
                Assert.That(hit.GetProperty("rule").GetString(), Is.EqualTo("SG003"));
                Assert.That(hit.GetProperty("file").GetString(), Is.EqualTo("Good.cs"));
                Assert.That(hit.GetProperty("line").GetInt32(), Is.GreaterThan(0));
                Assert.That(hit.GetProperty("message").GetString(), Is.Not.Empty);
            }
            finally { Directory.Delete(temp, true); }
        }

        [TestCase("{\"Schema\":1}")]
        [TestCase("{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[],\"supportSources\":[],\"policy\":{\"mode\":\"unsafe\"}}")]
        [TestCase("{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[\"relative.dll\"],\"supportSources\":[],\"policy\":{\"mode\":\"D1\"}}")]
        [TestCase("{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[],\"supportSources\":[],\"policy\":{\"mode\":\"D1\"},\"allowUnsafe\":true}")]
        [TestCase("{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[],\"supportSources\":[]}")]
        [TestCase("{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[],\"references\":[],\"supportSources\":[],\"policy\":{\"mode\":\"D1\"}}")]
        public void R2_11_StageIntStrictRequest(string request) => Assert.That(() => Rules.Parse(request), Throws.Exception);
    }
}
