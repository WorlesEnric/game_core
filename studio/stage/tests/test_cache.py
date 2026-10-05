"""R2-11: cache manifest and expanded dependency integrity are mandatory."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import sys
import unittest
import zipfile

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
