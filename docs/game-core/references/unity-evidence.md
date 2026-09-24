# Unity baseline evidence

Research date: **2026-09-25**. This ledger supports [Unity integration](../04-unity-integration.md). Package metadata and source archives were read from Unity's own registry/download service. No third-party mirror is the basis for the pins. No Unity Editor, package resolution, player build, or gameplay test was run as part of this documentation work.

## 1. Decision and limits of evidence

Choose **Unity 6000.0.75f1 + Entities 1.4.6 + Collections 2.6.6 + Burst 1.8.28 + Mathematics 1.3.2 + Test Framework 1.4.6**, with macOS 15.5 ARM64/Xcode 16.4/macOS SDK 15.5 as the first qualification host/target. This is a researched combination whose dependency declarations and published platform requirements align. Compatibility is not inferred from C# or .NET Standard alone.

The [6000.0.75f1 release page](https://unity.com/releases/editor/whats-new/6000.0.75f1) identifies the exact Editor and its installable platform modules. The [Unity 6.0 requirements](https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html) support the chosen host architecture and OS. [Apple's Xcode support table](https://developer.apple.com/xcode/system-requirements) lists Xcode 16.4, its macOS 15.5 SDK, and a host range containing macOS 15.5. [Unity's IL2CPP documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/il2cpp-introduction.html) requires native platform tooling and supports macOS-hosted macOS builds.

Unity's 6000.0 package listings identify the [Entities 1.4 line](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.entities.html), [Collections 2.6 line](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.collections.html), and [Burst 1.8 line](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.burst.html) as released for this Editor line. Those live listings had advanced to newer patch versions when researched. Exact older pins below are established by their registry version metadata and archives, not a claim that the live listings enumerate every historical patch.

This does not prove that all package transitive requirements resolve identically, that no Editor regression affects the integration, or that the generated application survives stripping. Those are first-wave experiments. The qualification report must record actual build numbers rather than filling them with guessed values. The selected desktop target is a risk-validation target, not an inferred product/platform commitment.

## 2. Exact registry metadata inspected

Each registry endpoint exposes version-indexed metadata. The rows below transcribe fields of the selected **version object**, not registry dist-tags.

| Package/version | Official metadata | Minimum Editor field | Relevant dependency fields |
| --- | --- | --- | --- |
| `com.unity.entities` **1.4.6** | [Entities registry](https://packages.unity.com/com.unity.entities) | `unity: 2022.3`, `unityRelease: 20f1` | Burst 1.8.28; Collections 2.6.6; Mathematics 1.3.2; Serialization 3.1.3; Profiling Core 1.0.3; Mono Cecil 1.11.6; Scriptable Build Pipeline 1.23.1; Performance Testing 3.0.3 |
| `com.unity.collections` **2.6.6** | [Collections registry](https://packages.unity.com/com.unity.collections) | `unity: 2022.3`, `unityRelease: 20f1` | Burst 1.8.28; Mathematics 1.3.2; Test Framework 1.4.6; Mono Cecil 1.11.6; Performance Testing 3.0.3 |
| `com.unity.burst` **1.8.28** | [Burst registry](https://packages.unity.com/com.unity.burst) | `unity: 2022.3` | Mathematics 1.2.1; JSON Serialize module 1.0.0 |
| `com.unity.mathematics` **1.3.2** | [Mathematics registry](https://packages.unity.com/com.unity.mathematics) | `unity: 2021.3` | No dependencies |
| `com.unity.test-framework` **1.4.6** | [Test Framework registry](https://packages.unity.com/com.unity.test-framework) | `unity: 2019.4`, `unityRelease: 1f1` | NUnit extension 2.0.3; IMGUI module 1.0.0; JSON Serialize module 1.0.0 |

Entities also declares the Audio, Physics, UIElements, AssetBundle, UnityAnalytics, and UnityWebRequest built-in modules at 1.0.0. These package-level module dependencies do not mandate gameplay audio/physics phases. The direct Mathematics 1.3.2 pin is higher than Burst's dependency declaration; the actual UPM resolution and source compilation must confirm the final graph. The registry table is **not** a substitute for the Editor's lock file.

| Exact archive | Registry `dist.shasum` (SHA-1) |
| --- | --- |
| [Entities 1.4.6](https://download.packages.unity.com/com.unity.entities/-/com.unity.entities-1.4.6.tgz) | `e90944159b948955251426b7537d161d93c6c50a` |
| [Collections 2.6.6](https://download.packages.unity.com/com.unity.collections/-/com.unity.collections-2.6.6.tgz) | `9796e5ee0d9ef6933fcf11656c00048965f09994` |
| [Burst 1.8.28](https://download.packages.unity.com/com.unity.burst/-/com.unity.burst-1.8.28.tgz) | `07790c2d06d99e35fe39459bd582a965d07700c4` |
| [Mathematics 1.3.2](https://download.packages.unity.com/com.unity.mathematics/-/com.unity.mathematics-1.3.2.tgz) | `8017b507cc74bf0a1dd14b18aa860569f807314d` |
| [Test Framework 1.4.6](https://download.packages.unity.com/com.unity.test-framework/-/com.unity.test-framework-1.4.6.tgz) | `5ac417e07314c8f6afba8109738c32b82d391e68` |

The Entities archive was downloaded and its SHA-1 matched the registry value. Its computed SHA-256 was `1f67ba09d6e52eb6d575b796a3e65b1cd6cec11b9f4d9657aa43cab0e0b790ec` (16,642,620 bytes). The other table hashes were read from official metadata, not recomputed from downloaded archives. These distinctions matter when reproducing the research.

Inspected exact Entities 1.4.6 archive paths included `Unity.Entities/DefaultWorldInitialization.cs`, `Unity.Entities/ScriptBehaviourUpdateOrder.cs`, `Unity.Entities/ComponentSystemGroup.cs`, `Unity.Entities/SystemBase.cs`, `Unity.Entities/WorldUnmanaged.cs`, `Documentation~/systems-icustombootstrap.md`, and `Documentation~/systems-update-order.md`. They establish that a successful custom bootstrap suppresses default initialization, the injection world must be assigned, explicit world PlayerLoop append/removal APIs exist, and manual driving is supported. They do not implement Game Core's own driver.

The exact `ComponentSystemGroup` source also shows that its stock per-system dispatch catches exceptions, logs them, and continues; `EnableSystemSorting` has a protected setter. Consequently the Game Core group subclass sets that property itself and overrides dispatch with its own guarded ordered table instead of relying on an exception outside `base.OnUpdate()`. The public unmanaged entry point is `SystemHandle.Update(WorldUnmanaged)`; the underlying `WorldUnmanagedImpl.UpdateSystem` is internal. The directly dispatched managed/unmanaged update paths restore their bookkeeping and propagate managed exceptions. This is a source-inspected integration constraint; the promised failstop behavior still needs the throwing-stage integration test.

## 3. Changelog and documentation version discipline

The official [Entities changelog](https://docs.unity3d.com/Packages/com.unity.entities@1.4/changelog/CHANGELOG.html) records **1.4.6 on 2026-04-13**, including the Burst 1.8.28 dependency update and a fix for memory corruption when ECB playback was interrupted after dynamic-buffer commands. This strengthens the choice of 1.4.6 over older pins for a lifecycle-heavy runtime; it does not warrant recovery by replaying a partly applied ECB.

Unity's `@1.4` web documentation is a release-line URL. At research time its header reported **1.4.8**, not 1.4.6. Likewise `@1.8` Burst documentation may advance within its release line. Therefore:

1. Exact version/dependency statements use the official registry version objects and exact archives.
2. Linked API/manual pages explain the named APIs and concepts; they are not silently treated as immutable 1.4.6 snapshots.
3. Any disputed API used by implementation must be checked against the resolved 1.4.6 package sources before merging.
4. The first-wave build is the executable compatibility proof. No successful build is inferred from a web page.

No upstream Git commit is claimed: the package archive version plus verified archive hash identifies the exact Entities source inspected. Unity does not require a guessed mirror commit to identify a registry package.

## 4. Primary-source claim index

| Source | What it supports; what remains our design decision |
| --- | --- |
| [C# compiler](https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html), [API profiles](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnet-profile-support.html) | Editor C# 9 and supported managed API profiles; use .NET Standard 2.1 deliberately, while proving AOT separately |
| [Custom bootstrap](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-icustombootstrap.html) | Bootstrap return/injection/manual-driving behavior; sole application pump and host ownership are Game Core choices |
| [System ordering](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-update-order.html), [ComponentSystemGroup](https://docs.unity3d.com/Packages/com.unity.entities@1.4/api/Unity.Entities.ComponentSystemGroup.html) | Group ordering constraints and APIs; runtime DAG flattening and one concrete system instance per world are adapter choices |
| [Jobs dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/job-system-job-dependencies.html) | Handle-based scheduling/completion; ownership, semantic order, and publication fences are protocol decisions |
| [ECB overview](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-entity-command-buffers.html) | Deferred structural commands and temporary-entity restrictions; no domain transaction semantics are inferred |
| [Burst types](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html), [generic jobs](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-generic-jobs.html), [player builds](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/building-projects.html) | Restricted compilation subset, concrete generic use/registration, target/build limitations; build logs must prove actual AOT coverage |
| [Baking](https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/baking-overview.html) | Editor conversion workflow; reusable eligibility schemas and runtime recipe activation are Game Core design |
| [Scripting restrictions](https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html), [linker preservation](https://docs.unity3d.com/6000.0/Documentation/Manual/managed-code-stripping-preserving.html) | AOT/generic and stripping constraints; generated closed registries are the selected solution |
| [Domain reload](https://docs.unity3d.com/6000.0/Documentation/Manual/domain-reloading.html) | Static/subscription reset requirements with reload disabled; protocol world generations and host reset are project responsibilities |
| [PhysicsScene simulation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.Simulate.html) | Explicit simulation of a physics scene; dedicated local scene and single-authority mapping are optional adapter decisions |
| [Test runner CLI](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html) | Batch test-run arguments; no test execution is implied by documenting them |

## 5. Required prototype evidence

Before treating the baseline as implementation-qualified, capture: a clean resolved package lock; source-generator compilation; a Burst-compiled scheduled job; correct custom bootstrap counts; a High-stripping IL2CPP standalone that mounts an initially absent precompiled plugin; runtime recipe instantiation; generic serialization/job coverage; and repeatable disposal with reload disabled. Validate physical-simulation timing separately when the optional action adapter is added. Do not block card/narrative validation on physics or rendering packages.

The fallback for a package/compiler incompatibility is a reviewed update of the **whole pinned baseline** to another officially documented compatible combination and rerun of the same minimal probe. It is not silent package downgrading, removing IL2CPP, or building an alternative ECS backend.
