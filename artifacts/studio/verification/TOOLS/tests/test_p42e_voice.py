import os
from pathlib import Path
import subprocess
import tempfile
import unittest

TOOLS=Path(__file__).resolve().parents[1]
class VoicePlayback(unittest.TestCase):
    def test_R2_38_P42e_failed_move_does_not_starve_distinct_destructive_take(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); (root/'voice').mkdir(); binaries=root/'bin';binaries.mkdir()
            stubs={
                'wpctl':'case "$1" in inspect) echo "id 10, type PipeWire:Interface:Node";; status) printf "Sources:\\n 20. gc_p42c_mic\\nSource endpoints:\\n";; esac',
                'pw-loopback':'exec sleep 30',
                'pw-play':'echo "$*" >> "$VOICE_TEST_OUT/play-calls"',
            }
            for name,body in stubs.items():
                p=binaries/name;p.write_text('#!/bin/bash\n'+body+'\n');p.chmod(0o755)
            command='''touch "$VOICE_TEST_OUT/voice/move-transcript.json"
touch "$VOICE_TEST_OUT/voice/play-destructive"
for _ in $(seq 1 70); do
  test ! -f "$VOICE_TEST_OUT/voice/played-destructive" || exit 0
  sleep 0.1
done
exit 9'''
            wrapper='voice-p42c.sh' if os.environ.get('P42E_VOICE_BEFORE') else 'voice-p42e.sh'
            result=subprocess.run(['bash',str(TOOLS/wrapper),str(root),'bash','-c',command],env=dict(os.environ,PATH=str(binaries)+':'+os.environ['PATH'],VOICE_TEST_OUT=str(root)),capture_output=True,text=True,timeout=15)
            self.assertEqual(result.returncode,0,result.stderr)
            calls=(root/'play-calls').read_text().splitlines()
            self.assertEqual(len(calls),1)
            self.assertIn('destructive.wav',calls[0])
            self.assertFalse((root/'voice/played-move').exists())
