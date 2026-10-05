#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GameCore.Stage.Analysis
{
    public sealed class Finding
    {
        public string RuleId { get; }
        public string File { get; }
        public int Line { get; }
        public string Message { get; }
        public Finding(string id, SyntaxNode node, string message)
        {
            RuleId = id;
            File = node.SyntaxTree.FilePath;
            Line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Message = message; // Fixed text only: candidate source and literals are never echoed.
        }
    }

    public sealed class Scan
    {
        private readonly string package;
        public Scan(string packageName) { package = packageName; }
        public IReadOnlyList<Finding> Run(IEnumerable<SyntaxTree> sources, IEnumerable<MetadataReference> references,
            IEnumerable<SyntaxTree>? support = null)
        {
            var trees = sources.ToArray();
            var candidates = new HashSet<SyntaxTree>(trees);
            var compilation = CSharpCompilation.Create("StageAnalysis", trees.Concat(support ?? Array.Empty<SyntaxTree>()),
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var findings = new List<Finding>();
            var approved = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var methods = trees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
            foreach (var declaration in methods)
            {
                var method = compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);
                if (method != null && (method.GetAttributes().Any(a => Trusted(a.AttributeClass, candidates) && (
                    a.AttributeClass?.ToDisplayString() == "GameCore.Gameplay.Contracts.AuthorOperationAttribute" ||
                    a.AttributeClass?.ToDisplayString() == "GameCore.Gameplay.Contracts.AuthorValidatorAttribute" ||
                    a.AttributeClass?.ContainingNamespace.ToDisplayString() == "NUnit.Framework" && declaration.SyntaxTree.FilePath.Split('/').Contains("Tests"))) ||
                    method.ContainingType.AllInterfaces.Any(i => Trusted(i, candidates) && i.Name == "ICatalogContributor" && i.ContainingNamespace.ToDisplayString().StartsWith("GameCore.", StringComparison.Ordinal))))
                    approved.Add(method);
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var declaration in methods)
                {
                    var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
                    var method = semantic.GetDeclaredSymbol(declaration);
                    if (method == null || !approved.Contains(method)) continue;
                    foreach (var call in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                        if (semantic.GetSymbolInfo(call).Symbol is IMethodSymbol target && approved.Add(target)) changed = true;
                }
            } while (changed);
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                void Report(string id, SyntaxNode node, string message) => findings.Add(new Finding(id, node, message));
                if (root.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.DisabledTextTrivia)))
                    Report("SG000", root, "Conditional inactive code is not admitted; submit a single audited source variant.");
                foreach (var error in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                    Report("SG000", root.FindNode(error.Location.SourceSpan), "Invalid C# 9 syntax.");
                foreach (var node in root.DescendantNodes())
                {
                    if (node is AttributeSyntax attr)
                    {
                        var attributeType = model.GetTypeInfo(attr).Type;
                        if (attributeType == null || attributeType.TypeKind == TypeKind.Error)
                            Report("SG012", node, "Attribute cannot be resolved with the trusted reference context.");
                        var type = attributeType?.ToDisplayString() ?? "";
                        if (type.StartsWith("UnityEditor.", StringComparison.Ordinal) ||
                            type == "UnityEditor.Callbacks.DidReloadScripts" || type == "UnityEditor.Callbacks.DidReloadScriptsAttribute" ||
                            type == "UnityEditor.MenuItem" || type == "UnityEditor.MenuItemAttribute" ||
                            type == "System.Runtime.CompilerServices.ModuleInitializerAttribute" ||
                            type == "UnityEngine.ExecuteAlways" || type == "UnityEngine.ExecuteInEditMode")
                            Report("SG001", node, "Automatic editor hooks and menu entry points are forbidden.");
                        if (type == "System.Runtime.InteropServices.DllImportAttribute" || type == "System.Runtime.InteropServices.LibraryImportAttribute")
                            Report("SG007", node, "Native imports are forbidden.");
                    }
                    if (node is BaseTypeSyntax baseType)
                    {
                        if (model.GetTypeInfo(baseType.Type).Type?.TypeKind == TypeKind.Error)
                            Report("SG012", node, "Base type cannot be resolved with the trusted reference context.");
                        for (var type = model.GetTypeInfo(baseType.Type).Type as INamedTypeSymbol; type != null; type = type.BaseType)
                            if (type.ToDisplayString() == "UnityEditor.AssetPostprocessor" || type.ToDisplayString() == "UnityEditor.AssetModificationProcessor")
                                Report("SG001", node, "Editor importer inheritance is forbidden.");
                    }
                    if (node.ChildTokens().Any(t => t.IsKind(SyntaxKind.UnsafeKeyword)) || node is PointerTypeSyntax || node is FunctionPointerTypeSyntax)
                        Report("SG006", node, "Unsafe code is forbidden.");
                    if (node is VariableDeclaratorSyntax variable && model.GetDeclaredSymbol(variable) is IFieldSymbol field && field.IsStatic && !field.IsConst)
                    {
                        // readonly does not make an array/collection/reference immutable.
                        if (!field.IsReadOnly || !ImmutableValue(field.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
                            if (!Singleton(field.ContainingType, candidates)) Report("SG008", node, "Static mutable state is forbidden.");
                    }
                    if (node is VariableDeclaratorSyntax initialized && initialized.Initializer != null &&
                        model.GetDeclaredSymbol(initialized) is IFieldSymbol initializedField && initializedField.IsStatic && !initializedField.IsConst &&
                        tree.FilePath.Split('/').Contains("Editor"))
                        Report("SG010", node, "Editor static field initialization is forbidden.");
                    if (node is PropertyDeclarationSyntax prop && model.GetDeclaredSymbol(prop) is IPropertySymbol property && property.IsStatic &&
                        prop.AccessorList != null && prop.AccessorList.Accessors.Any(a => a.Body == null && a.ExpressionBody == null) &&
                        (property.SetMethod != null || !ImmutableValue(property.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default))))
                        Report("SG008", node, "Static auto-property state is forbidden.");
                    if (node is EventFieldDeclarationSyntax evt && evt.Modifiers.Any(SyntaxKind.StaticKeyword))
                        Report("SG008", node, "Static event state is forbidden.");
                    if (node is ConstructorDeclarationSyntax ctor && ctor.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                        tree.FilePath.Split('/').Contains("Editor"))
                        Report("SG010", node, "Editor static initialization is forbidden.");

                    var symbol = model.GetSymbolInfo(node).Symbol;
                    var owner = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                    var full = owner?.ToDisplayString() ?? "";
                    var ns = owner?.ContainingNamespace?.ToDisplayString() ?? (symbol as INamespaceSymbol)?.ToDisplayString() ?? "";
                    if (owner != null && IsIo(ns) && full != "System.IO.File" && full != "System.IO.Directory" &&
                        full != "System.IO.Path" && full != "System.IO.MemoryStream" && full != "System.IO.StringReader" &&
                        full != "System.IO.StringWriter" && full != "System.IO.Stream" && !ExceptionType(owner))
                        Report("SG005", node, "Filesystem handle types are forbidden.");
                    if (ns == "System.Reflection.Emit" || ns.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal))
                        Report("SG002", node, "Reflection emit is forbidden.");
                    if (full == "System.Environment" && symbol?.Name == "CurrentDirectory")
                        Report("SG005", node, "Changing or depending on the process working directory is forbidden.");
                    if (full == "System.Diagnostics.Process" || full == "System.Diagnostics.ProcessStartInfo" || full == "System.Runtime.InteropServices.NativeLibrary")
                        Report("SG003", node, "Process execution is forbidden.");
                    if (ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal) || ns == "UnityEngine.Networking" || full == "UnityEngine.WWW")
                        Report("SG004", node, "Networking is forbidden.");
                    if (node is InvocationExpressionSyntax invocation)
                    {
                        var operation = model.GetOperation(invocation) as IInvocationOperation;
                        var method = operation?.TargetMethod;
                        if (method == null && !(model.GetOperation(invocation) is INameOfOperation))
                            Report("SG012", node, "Invocation cannot be resolved with the trusted reference context.");
                        if (ns == "UnityEditor" || ns.StartsWith("UnityEditor.", StringComparison.Ordinal))
                        {
                            var enclosing = model.GetEnclosingSymbol(node.SpanStart) as IMethodSymbol;
                            if (enclosing == null || !approved.Contains(enclosing))
                                Report("SG010", node, "Editor APIs require a declared author operation, validator or catalog contributor call chain.");
                        }
                        if (method?.ContainingType.ToDisplayString() == "UnityEngine.Resources" && method.Name.StartsWith("Load", StringComparison.Ordinal))
                        {
                            var value = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                            var constant = value == null ? default : model.GetConstantValue(value);
                            if (!constant.HasValue || !(constant.Value is string path) || !Relative(path))
                                Report("SG009", node, "Resources.Load requires a normalized relative constant path.");
                        }
                        if (full == "System.IO.Path" && method?.Name == "GetTempFileName" ||
                            IsIo(ns) && (method?.Name == "CreateSymbolicLink" || method?.Name == "CreateHardLink"))
                            Report("SG005", node, "Filesystem links and implicit temporary files are forbidden.");
                        if (IsIo(ns) && full != "System.IO.Path" && full != "System.IO.MemoryStream" && full != "System.IO.StringReader" && full != "System.IO.StringWriter")
                        {
                            var paths = operation?.Arguments.Where(a => a.Parameter?.Type.SpecialType == SpecialType.System_String &&
                                (a.Parameter.Name.Contains("path", StringComparison.OrdinalIgnoreCase) || a.Parameter.Name.Contains("file", StringComparison.OrdinalIgnoreCase))).ToArray();
                            var patternsSafe = operation == null || operation.Arguments.Where(a => a.Parameter?.Name == "searchPattern").All(a =>
                                a.Value.ConstantValue.HasValue && a.Value.ConstantValue.Value is string pattern && Relative(pattern) && !pattern.Contains('/'));
                            if ((full != "System.IO.File" && full != "System.IO.Directory") || paths == null || paths.Length == 0 || !patternsSafe ||
                                method?.Name == "SetCurrentDirectory" || method?.Name == "GetParent" ||
                                paths.Any(a => !SafePath(a.Value.Syntax as ExpressionSyntax, model, candidates)))
                                Report("SG005", node, "Filesystem access must prove every path stays within package Assets or persistentDataPath.");
                        }
                        if ((ns == "System.Reflection" && method?.Name != "GetCustomAttributes") ||
                            (full == "System.Activator") || operation == null && model.GetTypeInfo(invocation.Expression).Type?.TypeKind == TypeKind.Dynamic)
                            Report("SG011", node, "Dynamic or reflective invocation is forbidden.");
                    }
                    if (node is ObjectCreationExpressionSyntax && IsIo(ns) && owner != null && !ExceptionType(owner) &&
                        full != "System.IO.MemoryStream" && full != "System.IO.StringReader" && full != "System.IO.StringWriter")
                        Report("SG005", node, "Filesystem object handles are forbidden; use checked File or Directory operations.");
                    // Capturing a forbidden IO API as a delegate evades invocation argument checking.
                    if (symbol is IMethodSymbol && IsIo(ns) && full != "System.IO.Path" &&
                        (node is MemberAccessExpressionSyntax || node is SimpleNameSyntax) && !Invoked(node))
                        Report("SG005", node, "Filesystem method groups are forbidden.");
                }
            }
            return findings.GroupBy(f => (f.RuleId, f.File, f.Line)).Select(g => g.First()).OrderBy(f => f.File).ThenBy(f => f.Line).ThenBy(f => f.RuleId).ToArray();
        }

        private static bool ImmutableValue(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
        {
            if (type.SpecialType == SpecialType.System_String || type.TypeKind == TypeKind.Enum) return true;
            if (!type.IsValueType) return false;
            if (type.SpecialType != SpecialType.None) return true;
            if (!visiting.Add(type)) return true;
            return type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).All(f => ImmutableValue(f.Type, visiting));
        }
        private static bool IsIo(string ns) => ns == "System.IO" || ns.StartsWith("System.IO.", StringComparison.Ordinal);
        private static bool ExceptionType(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.ToDisplayString() == "System.Exception") return true;
            return false;
        }
        private static bool Trusted(ISymbol? symbol, HashSet<SyntaxTree> candidates) => symbol != null &&
            !symbol.DeclaringSyntaxReferences.Any(r => candidates.Contains(r.SyntaxTree));
        private static bool Invoked(SyntaxNode node)
        {
            while (node.Parent is MemberAccessExpressionSyntax) node = node.Parent;
            return node.Parent is InvocationExpressionSyntax call && call.Expression == node;
        }
        private static bool Singleton(INamedTypeSymbol type, HashSet<SyntaxTree> candidates)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
                if (Trusted(current, candidates) && current.OriginalDefinition.ToDisplayString() == "UnityEditor.ScriptableSingleton<T>") return true;
            return false;
        }
        private static bool Relative(string text) => text.Length > 0 && !text.Contains('\\') && !text.Contains(':') &&
            text.Split('/').All(p => p.Length > 0 && p != "." && p != "..");
        private bool SafePath(ExpressionSyntax? expression, SemanticModel model, HashSet<SyntaxTree> candidates)
        {
            if (expression == null) return false;
            var constant = model.GetConstantValue(expression);
            if (constant.HasValue && constant.Value is string text)
                return Relative(text) && (text == "Assets/" + package || text.StartsWith("Assets/" + package + "/", StringComparison.Ordinal));
            if (model.GetSymbolInfo(expression).Symbol is IPropertySymbol property &&
                Trusted(property, candidates) && property.ContainingType.ToDisplayString() == "UnityEngine.Application" && property.Name == "persistentDataPath") return true;
            if (expression is InvocationExpressionSyntax call && model.GetOperation(call) is IInvocationOperation op &&
                Trusted(op.TargetMethod, candidates) && op.TargetMethod.ContainingType.ToDisplayString() == "System.IO.Path" && op.TargetMethod.Name == "Combine")
            {
                var args = call.ArgumentList.Arguments;
                return args.Count >= 2 && SafePath(args[0].Expression, model, candidates) && args.Skip(1).All(a =>
                    model.GetConstantValue(a.Expression) is var value && value.HasValue && value.Value is string suffix && Relative(suffix));
            }
            return false; // Unknown aliases, mutable locals, joins and traversal are not proofs.
        }
    }
}
