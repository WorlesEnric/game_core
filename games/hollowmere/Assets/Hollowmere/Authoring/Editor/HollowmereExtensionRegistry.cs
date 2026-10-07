// Reviewed game-owned extension declarations. No candidate registers itself or supplies an executable member name.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.Contracts;
using Hollowmere.Boot;
using GameCore.Gameplay.World;
using Newtonsoft.Json.Linq;
using UnityEditor.Compilation;
using UnityEngine;

namespace Hollowmere.Authoring
{
    /// <summary>Recreated from trusted source after every reload; no mutable process-wide registration state.</summary>
    public sealed class HollowmereExtensionRegistry : IHollowmereExtensionSource
    {
        public const string PressurePlateType = "Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke";
        public const string PressurePlatePackage = "com.hollowmere.mechanism.pressureplate";
        public const string LeverType = "Hollowmere.Mechanism.Lever.LeverSmoke";
        public const string LeverPackage = "com.hollowmere.mechanism.lever";
        public const string LeverAssembly = "Hollowmere.Mechanism.Lever";
        public const string LeverExtensionType = "Hollowmere.Mechanism.Lever.LeverWorldExtension";
        public const string LeverCatalogType = "Hollowmere.Mechanism.Lever.Generated.LeverCatalog";
        public const string LeverCatalogAssembly = "Hollowmere.Mechanism.Lever.Generated";

        public HollowmereExtensionRegistry()
        {
            Entries = Array.AsReadOnly(new[]
            {
                new Entry(PressurePlateType, "Begin", PressurePlatePackage, null, null, 1),
                new Entry(LeverType, "Begin", LeverPackage, LeverAssembly, LeverExtensionType, 4),
            });
        }

        /// <summary>Only this trusted assembly declares entries. Candidate metadata can select, never add, an entry.</summary>
        public IReadOnlyList<Entry> Entries { get; }

        public Entry? Find(string smokeType, string smokeMethod)
        {
            foreach (Entry entry in Entries)
                if (string.Equals(entry.SmokeType, smokeType, StringComparison.Ordinal)
                    && string.Equals(entry.SmokeMethod, smokeMethod, StringComparison.Ordinal))
                    return entry;
            return null;
        }

        /// <summary>Normal world composition, before boot. An absent package contributes nothing.</summary>
        public void Contribute(WorldBuildOptions options, object? host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            foreach (Entry entry in Entries)
            {
                if (entry.ExtensionType == null) continue;
                try
                {
                    Type? type = entry.ResolveExtensionType();
                    if (type == null) continue;
                    if (!(Activator.CreateInstance(type) is IGameplayWorldExtension extension))
                        throw new InvalidOperationException("The trusted extension has no public parameterless constructor: " + entry.ExtensionType);
                    options.Extensions.Add(extension);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    throw new InvalidOperationException("Composing trusted extension " + entry.ExtensionType + " failed: " + error.Message, error);
                }
            }
        }

        public ICatalog ComposeCatalog(ICatalog world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            Entry lever = Find(LeverType, "Begin")!;
            if (lever.ResolveExtensionType() == null) return world;
            Type? type = Type.GetType(LeverCatalogType + ", " + LeverCatalogAssembly, false);
            if (type == null) throw new InvalidOperationException("The trusted lever generated catalog is unavailable");
            lever.RequirePackageAssembly(type, LeverCatalogAssembly);
            MethodInfo? build = type.GetMethod("BuildCatalog", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            FieldInfo? fingerprint = type.GetField("CatalogFingerprint", BindingFlags.Public | BindingFlags.Static);
            if (build == null || build.ReturnType != typeof(CatalogBuildResult) || fingerprint == null || !fingerprint.IsLiteral
                || !(fingerprint.GetRawConstantValue() is string declared))
                throw new InvalidOperationException("The trusted lever catalog does not expose its generated contract");
            CatalogBuildResult? result;
            try
            {
                result = (CatalogBuildResult?)build.Invoke(null, null);
            }
            catch (TargetInvocationException error) when (!(error.InnerException is OutOfMemoryException))
            {
                throw new InvalidOperationException("The trusted lever generated catalog refused to build", error.InnerException ?? error);
            }
            if (result?.Catalog == null || result.Catalog.Fingerprint.ToHex() != declared)
                throw new InvalidOperationException("The trusted lever catalog fingerprint differs from its generated constant");
            return new HollowmereCompositeCatalog(world, new[] { result.Catalog });
        }

        public sealed class Entry
        {
            internal Entry(string smokeType, string smokeMethod, string package, string? assembly, string? extensionType, int minimumSteps)
            {
                SmokeType = smokeType;
                SmokeMethod = smokeMethod;
                Package = package;
                Assembly = assembly;
                ExtensionType = extensionType;
                MinimumSteps = minimumSteps;
            }

            public string SmokeType { get; }
            public string SmokeMethod { get; }
            public string Package { get; }
            public string? Assembly { get; }
            public string? ExtensionType { get; }
            public int MinimumSteps { get; }

            internal Type? ResolveExtensionType()
            {
                if (ExtensionType == null || Assembly == null) return null;
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", Package));
                Type? type = Type.GetType(ExtensionType + ", " + Assembly, false);
                if (type == null)
                {
                    if (Directory.Exists(root))
                        throw new InvalidOperationException("The installed package has no trusted extension: " + ExtensionType);
                    return null;
                }

                RequirePackageAssembly(type, Assembly);
                if (!type.IsPublic || !type.IsSealed || !typeof(IGameplayWorldExtension).IsAssignableFrom(type)
                    || type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException("The trusted extension does not implement its reviewed public contract: " + ExtensionType);
                return type;
            }

            internal void RequirePackageAssembly(Type type, string assembly)
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", Package));
                string? definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly);
                string manifest = Path.Combine(root, "package.json");
                if (!string.Equals(type.Assembly.GetName().Name, assembly, StringComparison.Ordinal)
                    || string.IsNullOrEmpty(definition)
                    || !Path.GetFullPath(Path.IsPathRooted(definition) ? definition : Path.Combine(Application.dataPath, "..", definition))
                        .StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || !File.Exists(manifest)
                    || !string.Equals((string?)JObject.Parse(File.ReadAllText(manifest))["name"], Package, StringComparison.Ordinal))
                    throw new InvalidOperationException("The trusted assembly is not owned by package " + Package);
            }

            internal LeverAccess BindLever(GameplayWorld world)
            {
                Type? type = ResolveExtensionType();
                if (type == null) throw new InvalidOperationException("The trusted lever extension is not installed");
                IGameplayWorldExtension? found = null;
                foreach (IGameplayWorldExtension extension in world.Extensions)
                {
                    if (extension.GetType() != type) continue;
                    if (found != null) throw new InvalidOperationException("The trusted lever extension is composed more than once");
                    found = extension;
                }
                if (found == null) throw new InvalidOperationException("The trusted lever extension is absent from the active world");
                return new LeverAccess(found);
            }
        }

        internal sealed class LeverAccess
        {
            private readonly Func<int> state;
            private readonly Func<bool> toggle;
            private readonly Func<bool> initialize;

            internal LeverAccess(IGameplayWorldExtension extension)
            {
                Extension = extension;
                Type type = extension.GetType();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                PropertyInfo? property = type.GetProperty("State", flags);
                MethodInfo? getter = property?.GetGetMethod();
                MethodInfo? method = type.GetMethod("Toggle", flags, null, Type.EmptyTypes, null);
                MethodInfo? initializer = type.GetMethod("InitializeNewTarget", flags, null, Type.EmptyTypes, null);
                if (property == null || property.PropertyType != typeof(int) || getter == null || property.GetIndexParameters().Length != 0
                    || method == null || method.ReturnType != typeof(bool)
                    || initializer == null || initializer.ReturnType != typeof(bool))
                    throw new InvalidOperationException("The trusted lever requires State, Toggle and InitializeNewTarget with their reviewed signatures");
                state = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), extension, getter);
                toggle = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), extension, method);
                initialize = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), extension, initializer);
            }

            internal IGameplayWorldExtension Extension { get; }
            internal int State => state();
            internal bool Toggle() => toggle();
            internal bool InitializeNewTarget() => initialize();
        }
    }
}
