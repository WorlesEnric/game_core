"""R2-11: cache manifest and expanded dependency integrity are mandatory."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import sys
import unittest
import zipfile
from unittest import mock

sys.path.insert(0, str(Path(__file__).parents[1]))
spec = importlib.util.spec_from_file_location('stage_cache', Path(__file__).parents[1] / 'cache.py')
cache = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cache)


class CacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.folder = self.root / 'nuget/example/1.0.0'
        self.folder.mkdir(parents=True)
        self.archive = self.folder / 'example.1.0.0.nupkg'
        with zipfile.ZipFile(self.archive, 'w') as z:
            z.writestr('lib/compiler.dll', b'trusted bytes')
        (self.folder / 'lib').mkdir()
        self.dll = self.folder / 'lib/compiler.dll'
        self.dll.write_bytes(b'trusted bytes')
        (self.folder / '.nupkg.metadata').write_text('{}')
        (self.folder / 'example.1.0.0.nupkg.sha512').write_text('unused')
        self.expected = {'schema': 'gamecore.stage.cache/1', 'packages': [
            {'id': 'Example', 'version': '1.0.0', 'sha512': base64.b64encode(hashlib.sha512(self.archive.read_bytes()).digest()).decode()}]}
        (self.root / 'cache-manifest.json').write_text(json.dumps(self.expected))

    def tearDown(self):
        self.temp.cleanup()

    def test_R2_11_StageIntPinnedClosure(self):
        cache.verify(self.root, self.expected)
        (self.root / 'cache-manifest.json').write_text('{}')
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def test_R2_11_StageIntArchiveTamper(self):
        self.archive.write_bytes(b'tampered')
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def test_R2_11_StageIntExpandedDllTamper(self):
        self.dll.write_bytes(b'tampered')
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def test_R2_11_StageIntExtraPackageRefused(self):
        (self.folder / 'extra.targets').write_text('untrusted')
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def test_R2_11_StageIntLinkRefused(self):
        self.dll.unlink()
        self.dll.symlink_to(self.archive)
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def test_R2_11_StageIntNuGetEscapedPayloadNames(self):
        with zipfile.ZipFile(self.archive, 'a') as z:
            z.writestr('lib/portable%2Btest/compiler.xml', b'documentation')
        target = self.folder / 'lib/portable+test/compiler.xml'
        target.parent.mkdir()
        target.write_bytes(b'documentation')
        self.expected['packages'][0]['sha512'] = base64.b64encode(hashlib.sha512(self.archive.read_bytes()).digest()).decode()
        (self.root / 'cache-manifest.json').write_text(json.dumps(self.expected))
        cache.verify(self.root, self.expected)
        target.write_bytes(b'tampered')
        with self.assertRaises(ValueError): cache.verify(self.root, self.expected)

    def provisioning_inputs(self):
        pins = self.root / 'tools/cache'
        pins.mkdir(parents=True)
        (pins / 'cache-lock.json').write_text(json.dumps(self.expected))
        for name in ('Dependencies.csproj', 'RulesDependencies.csproj', 'NuGet.Config', 'packages.lock.json', 'Rules.packages.lock.json'):
            (pins / name).write_text('{}')
        self.unity = self.root / 'unity'
        self.metadata = {'ScriptAssemblies/Unity.Fixture.dll': hashlib.sha256(b'metadata').hexdigest()}
        for relative in self.metadata:
            path = self.unity / relative
            path.parent.mkdir(parents=True)
            path.write_bytes(b'metadata')
        (self.unity / 'PackageCache/example').mkdir(parents=True)
        (self.unity / 'PackageCache/example/package.json').write_text('{}')
        (self.unity / 'ArtifactDB').write_bytes(b'must not grant warm status')
        metadata_lock = pins / 'unity-metadata-lock.json'
        metadata_lock.write_text(json.dumps(self.metadata))
        self.upm = self.root / 'public-upm'
        data = b'pinned public registry bytes'
        self.record = {'key': 'https://packages.unity.com/fixture',
                       'integrity': 'sha512-' + base64.b64encode(hashlib.sha512(data).digest()).decode(),
                       'sha256': hashlib.sha256(data).hexdigest(), 'size': len(data)}
        payload = cache.upm_cache.location(self.upm, self.record['integrity'])
        payload.parent.mkdir(parents=True)
        payload.write_bytes(data)
        for patch in (mock.patch.object(cache, 'HERE', pins.parent),
                      mock.patch.object(cache.analysis_context, 'MANIFEST', metadata_lock),
                      mock.patch.object(cache.upm_cache, 'manifest', return_value={'records': [self.record]})):
            patch.start()
            self.addCleanup(patch.stop)
        self.binary = self.root / 'companion'
        self.binary.write_text('''#!/usr/bin/python3
import pathlib,sys
root = pathlib.Path(sys.argv[sys.argv.index('--root') + 1])
version = pathlib.Path(__file__).with_name('version').read_text()
print(root / '_warm' / version)
''')
        self.binary.chmod(0o755)
        (self.root / 'version').write_text('a' * 64)
        self.stage_root = self.root / 'new-private-stage'
        return ['--stage-root', str(self.stage_root), '--repo', str(self.root),
                '--source-project', str(self.root), '--binary', str(self.binary)]

    def run_provisioner(self, args):
        with mock.patch.object(sys, 'argv', ['cache.py', *args]):
            cache.main()

    def seed_arguments(self):
        return ['--offline-from', str(self.root / 'nuget'), '--unity-library', str(self.unity),
                '--upm-from', str(self.upm)]

    def test_R7_A_ProvisionExactVersionAndPreserveColdGrace(self):
        args = self.provisioning_inputs()
        self.run_provisioner(args + self.seed_arguments())
        first = self.stage_root / '_warm' / ('a' * 64)
        cache.verify_complete(first)
        self.assertEqual((first / 'cache-manifest.json').read_bytes(), (self.root / 'cache-manifest.json').read_bytes())
        self.assertEqual((first / 'nuget/example/1.0.0/lib/compiler.dll').read_bytes(), b'trusted bytes')
        self.assertEqual((first / 'analysis-context/ScriptAssemblies/Unity.Fixture.dll').read_bytes(), b'metadata')
        self.assertFalse((first / 'Library/ArtifactDB').exists())
        marker = first / '.cold-grace-used'
        marker.write_bytes(b'consumed')
        self.run_provisioner(args + self.seed_arguments())
        self.assertEqual(marker.read_bytes(), b'consumed')
        (self.root / 'version').write_text('b' * 64)
        self.run_provisioner(args + self.seed_arguments())
        second = self.stage_root / '_warm' / ('b' * 64)
        cache.verify_complete(second)
        self.assertEqual(marker.read_bytes(), b'consumed')
        self.assertFalse((second / '.cold-grace-used').exists())
        self.assertFalse((self.stage_root / 'cache-manifest.json').exists())

    def test_R7_A_RejectForeignOrMalformedVersionBeforeWrites(self):
        args = self.provisioning_inputs()
        for version in ('../outside', 'not-a-version', 'A' * 64):
            with self.subTest(version=version):
                (self.root / 'version').write_text(version)
                with self.assertRaisesRegex(SystemExit, 'cache_invalid'):
                    self.run_provisioner(args + self.seed_arguments())
                self.assertFalse(self.stage_root.exists())

    def test_R7_A_RejectLinkedVersionedRootBeforeWrites(self):
        args = self.provisioning_inputs()
        destination = self.root / 'outside'
        destination.mkdir()
        self.stage_root.symlink_to(destination, target_is_directory=True)
        with self.assertRaisesRegex(SystemExit, 'cache_invalid'):
            self.run_provisioner(args + self.seed_arguments())
        self.assertEqual(list(destination.iterdir()), [])

    def test_R7_A_RequireBothOfflineUnityInputsBeforeProvisioning(self):
        args = self.provisioning_inputs()
        for seed in ([], ['--unity-library', str(self.unity)], ['--upm-from', str(self.upm)]):
            with self.subTest(seed=seed), self.assertRaises(SystemExit) as error:
                self.run_provisioner(args + seed)
            self.assertEqual(error.exception.code, 2)
            self.assertFalse(self.stage_root.exists())

    def test_R7_A_CompleteVerificationRejectsMissingAndCorruptInputs(self):
        args = self.provisioning_inputs()
        self.run_provisioner(args + self.seed_arguments())
        target = self.stage_root / '_warm' / ('a' * 64)
        paths = [target / 'nuget/example/1.0.0/lib/compiler.dll',
                 target / 'analysis-context/ScriptAssemblies/Unity.Fixture.dll',
                 cache.upm_cache.location(target / 'upm', self.record['integrity'])]
        for path in paths:
            original = path.read_bytes()
            for content in (None, b'tampered'):
                with self.subTest(path=path, content=content):
                    if content is None:
                        path.unlink()
                    else:
                        path.write_bytes(content)
                    with self.assertRaisesRegex(SystemExit, 'cache_invalid'):
                        self.run_provisioner(args + ['--verify'])
                    path.write_bytes(original)
        shutil.rmtree(target / 'Library/PackageCache')
        with self.assertRaisesRegex(SystemExit, 'cache_invalid'):
            self.run_provisioner(args + ['--verify'])

    def test_R7_A_InvalidSeedNeverPassesProvisioning(self):
        args = self.provisioning_inputs()
        self.dll.write_bytes(b'tampered expanded compiler')
        with self.assertRaisesRegex(SystemExit, 'cache_invalid'):
            self.run_provisioner(args + self.seed_arguments())
        self.assertFalse((self.stage_root / '_warm' / ('a' * 64) / 'cache-manifest.json').exists())
