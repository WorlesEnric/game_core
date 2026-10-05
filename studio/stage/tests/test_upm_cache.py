import base64
import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import upm_cache


class UpmTests(unittest.TestCase):
    def test_R2_11_StageIntPublicUpmMetadataAndMappingIntegrity(self):
        data = b'{"versions":{"1.0.0":{}}}'
        record = {'key':'package-metadata|com.unity.example|packages.unity.com',
                  'integrity':'sha512-' + base64.b64encode(hashlib.sha512(data).digest()).decode(),
                  'sha256':hashlib.sha256(data).hexdigest(), 'size':len(data)}
        with tempfile.TemporaryDirectory() as directory, patch.object(upm_cache, 'manifest', return_value={'records':[record]}):
            root = Path(directory)
            source = root / 'source'
            path = upm_cache.location(source, record['integrity'])
            path.parent.mkdir(parents=True)
            path.write_bytes(data)
            target = root / 'cache'
            upm_cache.provision(target, source)
            upm_cache.verify(target)
            path = upm_cache.location(target, record['integrity'])
            path.write_bytes(b'changed')
            with self.assertRaises(ValueError): upm_cache.verify(target)
            path.write_bytes(data)
            upm_cache.index_path(target, record['key']).write_text('\n')
            with self.assertRaises(ValueError): upm_cache.verify(target)
