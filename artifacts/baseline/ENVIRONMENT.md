# Baseline player environment — Linux IL2CPP qualification

This record captures the Linux build host and both StandaloneLinux64 IL2CPP shapes. The raw facts and build logs are in `artifacts/baseline/build/`; Unity package resolution and generated catalog reproducibility were checked on this host.

Evidence and release status is normative in
[P-060](../../docs/game-core/00-core-protocols.md#p-060): "The implementation records the exact build/catalog
versions, resolved dependencies, native compiler, architecture, backend/stripping settings, test commands, logs,
fixture hashes, and measurements." The V1 profile is
[P-058](../../docs/game-core/00-core-protocols.md#p-058), and the exact pins are
[04 §1](../../docs/game-core/04-unity-integration.md).

## 1. Recorded facts

| Item | Value | Where it comes from |
| --- | --- | --- |
| Source revision | `aad9038ae33a7183071905326f6dcc3c695736ab` | the commit the build ran on |
| Build date (UTC) | `2026-09-26T09:15:05Z` | `<ARTIFACTS>/host/environment.txt: date_utc` |
| Unity Editor revision | `6000.0.75f1` | `environment.txt: editor_version` |
| Editor install path | `/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity` | `environment.txt: editor` |
| Host OS and build | Ubuntu 24.04.4 LTS; kernel `7.0.0-31-generic` | `environment.txt: host`, `kernel` |
| Host architecture | `x86_64` | `environment.txt: arch` |
| Host CPU and core count | 12th Gen Intel Core i7-12700KF; 20 logical processors | `environment.txt: cpu`, `nproc` |
| IL2CPP scripting backend | `IL2CPP` | `BuildProbe.cs` sets it and reads it back |
| IL2CPP compiler configuration | `Release` | `BuildProbe.cs` |
| Managed stripping level | `High` | `BuildProbe.cs` |
| API compatibility level | `.NET Standard 2.1` | `BuildProbe.cs` |
| Burst | enabled (the build refuses to proceed when it is off) | `BuildProbe.cs` |
| Native C++ compiler | Unity clang `9.0.1` (`llvm-9.0.1-1`); host clang `18.1.3` | build DAG `Library/Bee/Player*.dag.json` and bundled `clang++ --version` |
| Linux sysroot / glibc | Unity sysroot `9.1.0-2.17-v0_608efc24a3b402ec57809211b16a6d32d519f891d4038e1fc8509fe300c395b2-1`, glibc 2.17; host glibc 2.39 | build DAG `--sysroot` and `getconf GNU_LIBC_VERSION` |
| Entities | `1.4.6` | `Packages/packages-lock.json` |
| Collections | `2.6.6` | `packages-lock.json` |
| Burst package | `1.8.28` | `packages-lock.json` |
| Mathematics | `1.3.2` | `packages-lock.json` |
| Unity Test Framework | `1.6.0` | `packages-lock.json` |
| Performance Testing | `3.0.3` | `packages-lock.json` |
| Linux IL2CPP toolchain | `2.0.11` | `packages-lock.json` |
| `packages-lock.json` SHA-256 | `726e06ab3dd55e5d70974779298f3835e0afac1aec83c6a26c821e437edfea63` | `environment.txt: packages_lock_sha256` |
| `manifest.json` SHA-256 | `301020ec2f42426b20deccfbdc2deb60abd28ef3b811d7c3e2bc7e23af8f2b84` | `environment.txt: packages_manifest_sha256` |
| `Assets/link.xml` SHA-256 | `f7d33b72301efecd088067476094bcb26ab562dc9b46f1fae898bfbe5764beb5` | `environment.txt: link_xml_sha256` |
| Player SHA-256 (qualification) | launcher `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; `GameAssembly.so` `308e9127e9430de95b3ef18e60d87c7bd30b65cf0fe14c46b9bf26fcf811e001` | `<ARTIFACTS>/qualification/environment.txt: player_sha256`; `sha256sum` on native library |
| Player SHA-256 (release) | launcher `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; `GameAssembly.so` `f2c1407f9f20abfbb6274f05af835a4cf180b0dbe25b1eea92a587cc8eaf209d` | `<ARTIFACTS>/release/environment.txt: player_sha256`; `sha256sum` on native library |
| Combined catalog fingerprint (per shape) | both `f776f27d633346bc3030f9350949c61d4432cda449193e9d95c142cb175797f2` | `<ARTIFACTS>/{qualification,release}/catalog-ledger.json: combined` |
| `.NET SDK` used for the pure assemblies | `8.0.425` | `environment.txt: dotnet_version` |

## 2. Generated registration fingerprints

The four committed catalogs and their two recorded hashes. `CatalogFileHash` covers the generated file prefix;
`CatalogFingerprint` covers the declarations the runtime catalog builds (P-028). Both are recomputed by
`tools/verify_generated_catalog.py` and compared across builds by `tools/compare_registration_fingerprints.py`.

| Catalog | Description | `CatalogFileHash` | `CatalogFingerprint` | Groups | Schemas |
| --- | --- | --- | --- | ---: | ---: |
| `ProbeCatalog` (narrative + fixture registrations) | `Catalogs/ProbeCatalog.catalog.json` | `03bdcd23fd8ec0515d93d7c54d7f7c695f3860a850a7280b82316aaacd14c5c1` | `02bb94ab81353a2063fb1cb92d77ce3b9889885b02a95de515193e45178f37ff` | 3 | 1 |
| `CardCatalog` | `Catalogs/CardCatalog.catalog.json` | `75a60968c3c59ac69e0c2b6b806c792f48e5b243a526b1ae29c7615e8c1a2e9e` | `77838f4766f9086a0fa32b4fb30d8d5fceeb3a8072e61594fa2177946ad3315d` | 5 | 1 |
| `CheckpointCatalog` | `Catalogs/CheckpointCatalog.catalog.json` | `08d43a031cd41a968daf1ea86633fc826956ae64a8a717e6d999d77e5cd5d2d9` | `2d931c984b3bcf3f995a0f88bc82708b0d07e4395fcad44019ebc033126a0eb5` | 0 | 13 |
| `TraversalCatalog` (GC-025) | `Catalogs/TraversalCatalog.catalog.json` | `3332a5e54f1e5beea869200672f423b0089c6ec2fb4991334090ab797c0a1f8c` | `f66d97d489bc1d8b891b20bc1c16186e0843d2ca5760dcd010909feea6829459` | 4 | 1 |

These literals are the committed values on this revision. The build host records what its own build *observed* in
`${ARTIFACTS}/fingerprint-comparison.json`; a difference between the two is the failure this baseline exists to
catch, not a number to overwrite here.

## 3. What the two shapes are

| Shape | Project | Manifest | Fixtures | Player |
| --- | --- | --- | --- | --- |
| qualification | `unity/GameCore.Validation` | both marker packages, replay package, test packages, 11 testables | every qualification probe fixture | `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64` |
| release | `unity/GameCore.ReleaseCheck` (disposable, gitignored, produced by `tools/unity/prepare_gc017_release_project.py`) | marker-free, test-free; Unity resolves its own marker-free lock | shipping surface only | `unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64` |

Both shapes are StandaloneLinux64 / IL2CPP / Release / High stripping / Burst-enabled; the difference is package and
fixture surface, never a compiler setting (`BuildProbe.cs` sets the settings and reads them back).

## 4. Commands

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
PROBE_RUNS=5 \
  tools/build_baseline_player.sh
```

Recorded values come from `artifacts/baseline/build/host/environment.txt`, both per-shape `environment.txt` files, the two `catalog-ledger.json` files and `fingerprint-comparison.json`. The native `GameAssembly.so` hashes distinguish the two shapes; the launcher hashes alone are identical.
