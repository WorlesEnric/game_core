#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace GameCore.Stage.Analysis.Tests
{
    public sealed class ScanTests
    {
        private const string Context = @"
namespace UnityEditor {
 public class InitializeOnLoadAttribute:System.Attribute {} public class InitializeOnLoadMethodAttribute:System.Attribute {}
 public class MenuItem:System.Attribute {public MenuItem(string x){}}
 public class AssetPostprocessor {} public class AssetModificationProcessor {}
 public class ScriptableSingleton<T> {} public static class AssetDatabase {public static void Refresh(){}}
}
namespace UnityEditor.Callbacks {public class DidReloadScripts:System.Attribute {}}
namespace UnityEngine {public static class Application {public static string persistentDataPath => ""/home/test"";}
 public static class Resources {public static object Load(string path)=>new object();}}
namespace GameCore.Gameplay.Contracts {
 public class AuthorOperationAttribute:System.Attribute {} public class AuthorValidatorAttribute:System.Attribute {}}
";
        private static MetadataReference[] References() => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        private static string[] Rules(string code, string path = "Fixture.cs", string? context = null) => new Scan("com.example.plate").Run(
            new[] { CSharpSyntaxTree.ParseText("#nullable enable\n" + code, new CSharpParseOptions(LanguageVersion.CSharp9), path) }, References(),
            new[] { CSharpSyntaxTree.ParseText(context ?? Context, new CSharpParseOptions(LanguageVersion.CSharp9)) }).Select(f => f.RuleId).Distinct().ToArray();

        [TestCase("using Hook=UnityEditor.InitializeOnLoadAttribute; [Hook] class C{}", "SG001")]
        [TestCase("class C { [UnityEditor.InitializeOnLoadMethod] static void M(){} }", "SG001")]
        [TestCase("class B:UnityEditor.AssetPostprocessor{} class C:B{}", "SG001")]
        [TestCase("class C:UnityEditor.AssetModificationProcessor{}", "SG001")]
        [TestCase("class C{[UnityEditor.Callbacks.DidReloadScripts] static void M(){} }", "SG001")]
        [TestCase("class C{[UnityEditor.MenuItem(\"x\")] static void M(){} }", "SG001")]
        [TestCase("using P=System.Diagnostics.Process; class C{void M(){P.Start(\"x\");}}", "SG003")]
        [TestCase("using E=System.Reflection.Emit.DynamicMethod; class C{E? x;}", "SG002")]
        [TestCase("using H=System.Net.Http.HttpClient; class C{void M(){new H();}}", "SG004")]
        [TestCase("class C{unsafe void M(){int* p=null;}}", "SG006")]
        [TestCase("class C{[System.Runtime.InteropServices.DllImport(\"x\")] static extern void M();}", "SG007")]
        [TestCase("class C{static int x;}", "SG008")]
        [TestCase("class C{static readonly int[] x=new int[1];}", "SG008")]
        [TestCase("class C{static int X{get;set;}}", "SG008")]
        [TestCase("class C{void M(){UnityEngine.Resources.Load(\"/root\");}}", "SG009")]
        [TestCase("class C{void M(){UnityEngine.Resources.Load(\"a/../b\");}}", "SG009")]
        [TestCase("class C{void M(){UnityEditor.AssetDatabase.Refresh();}}", "SG010")]
        [TestCase("class C{void M(){typeof(C).GetMethod(\"M\")!.Invoke(null,null);}}", "SG011")]
        public void R2_11_ForbiddenSemanticSymbols(string code, string rule) => Assert.That(Rules(code), Does.Contain(rule));

        [TestCase("System.IO.Path.GetTempFileName();")]
        [TestCase("System.IO.Directory.EnumerateFiles(\"Assets/com.example.plate\", \"../*\");")]
        [TestCase("System.IO.File.ReadAllText(\"/etc/passwd\");")]
        [TestCase("System.IO.File.WriteAllText(\"Assets/com.example.plate/../../escape\", \"x\");")]
        [TestCase("System.IO.File.WriteAllText(\"/outside\", UnityEngine.Application.persistentDataPath);")]
        [TestCase("System.IO.File.Copy(\"Assets/com.example.plate/in\", \"/out\");")]
        [TestCase("System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, \"../escape\"));")]
        [TestCase("System.Func<string,string> read=System.IO.File.ReadAllText;")]
        [TestCase("new System.IO.StreamReader(\"/outside\");")]
        [TestCase("string p=\"Assets/com.example.plate/a\";p=\"/outside\";System.IO.File.ReadAllText(p);")]
        public void R2_11_FilesystemPathsMustBeProven(string body) => Assert.That(Rules("class C{void M(){" + body + "}}"), Does.Contain("SG005"));

        [TestCase("System.IO.File.ReadAllText(\"Assets/com.example.plate/file.json\");")]
        [TestCase("System.IO.File.WriteAllText(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, \"data\", \"x.txt\"),\"value\");")]
        [TestCase("UnityEngine.Resources.Load(\"relative/file\");")]
        public void R2_11_SafePaths(string body) => Assert.That(Rules("class C{void M(){" + body + "}}"), Is.Empty);

        [TestCase("bin")]
        [TestCase("obj")]
        public void R2_11_CliScansBuildNamedDirectoriesAndUppercaseSources(string name)
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, name));
            try
            {
                File.WriteAllText(Path.Combine(root, "package.json"), "{\"name\":\"com.example.plate\"}");
                File.WriteAllText(Path.Combine(root, "rules.json"), "{\"schema\":\"gamecore.stage.analyze/1\",\"references\":[],\"supportSources\":[],\"policy\":{\"mode\":\"D1\"}}");
                File.WriteAllText(Path.Combine(root, name, "Bad.CS"), "class C{void M(){System.Diagnostics.Process.Start(\"blocked\");}}");
                var output = Path.Combine(root, "findings.json");
                Assert.That(Program.Main(new[] { "--root", root, "--rules", Path.Combine(root, "rules.json"), "--out", output }), Is.EqualTo(3));
                Assert.That(File.ReadAllText(output), Does.Contain("SG003"));
                File.WriteAllText(Path.Combine(root, "package.json"), "{\"name\":5}");
                Assert.That(Program.Main(new[] { "--root", root, "--rules", Path.Combine(root, "rules.json"), "--out", output }), Is.EqualTo(2));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void R2_11_ValueTypeContainingMutableReferenceRefuses() => Assert.That(Rules("struct Box{public System.Collections.Generic.List<int> Items;} class C{static readonly Box State = new Box();}"), Does.Contain("SG008"));

        [Test]
        public void R2_11_ForgedPathCombineRefuses() => Assert.That(Rules("namespace System.IO {public static class Path {public static string Combine(string a,string b) => \"/outside\";}} class C{void M(){System.IO.File.ReadAllText(System.IO.Path.Combine(\"Assets/com.example.plate\",\"x\"));}}", context: ""), Does.Contain("SG005"));

        [Test]
        public void R2_11_ForgedPersistentRootRefuses() => Assert.That(Rules("namespace UnityEngine {public static class Application {public static string persistentDataPath => \"/outside\";}} class C{void M(){System.IO.File.ReadAllText(UnityEngine.Application.persistentDataPath);}}", context: ""), Does.Contain("SG005"));

        [Test]
        public void R2_11_UnresolvedContextRefuses() => Assert.That(Rules("[MissingAttribute] class C : MissingBase {void M(){Missing.Call();}}"), Does.Contain("SG012"));

        [Test]
        public void R2_11_UsingStaticIoDelegateRefuses() => Assert.That(Rules("using static System.IO.File; class C{void M(){System.Func<string,string> f=ReadAllText;}}"), Does.Contain("SG005"));

        [Test]
        public void R2_11_InactiveCodeRefuses() => Assert.That(Rules("#if UNREVIEWED\nclass C{}\n#endif"), Does.Contain("SG000"));

        [Test]
        public void R2_11_CommittedNegativeFixtures()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "samples"))) directory = directory.Parent;
            Assert.That(directory, Is.Not.Null);
            var fixture = Path.Combine(directory!.FullName, "samples/mechanisms/negative-semantic");
            var trees = Directory.GetFiles(fixture, "*.cs").Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.CSharp9), Path.GetFileName(p)));
            var ids = new Scan("com.example.semantic-negative").Run(trees, References(),
                new[] { CSharpSyntaxTree.ParseText(Context, new CSharpParseOptions(LanguageVersion.CSharp9)) }).Select(f => f.RuleId).Distinct().ToArray();
            var expected = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(fixture, "expected.json")))!;
            Assert.That(ids, Is.SupersetOf(expected));
        }

        [Test]
        public void R2_11_AuthorOperationHelpers() => Assert.That(Rules("class C{[GameCore.Gameplay.Contracts.AuthorOperation] static void M(){Helper();} static void Helper(){UnityEditor.AssetDatabase.Refresh();}}", "Editor/C.cs"), Is.Empty);

        [Test]
        public void R2_11_EditorStaticConstructor() => Assert.That(Rules("class C{static C(){}}", "Editor/C.cs"), Does.Contain("SG010"));

        [Test]
        public void R2_11_NoStringOrCommentFalsePositive() => Assert.That(Rules("class C{const string S=\"System.Diagnostics.Process.Start /etc/passwd\"; /* UnityEditor.InitializeOnLoad */}"), Is.Empty);
    }
}
