#!/usr/bin/env python3
"""Package the committed semantic-negative sources for refusal testing; never import/admit them."""
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile
import verify as v

base=v.ROOT/'samples/mechanisms/pressure-plate/candidate'
out=v.OUT/'W-MECH-01/negative-semantic-input'
(out/'artifacts').mkdir(parents=True,exist_ok=True)
files={}
with tarfile.open(base/'artifacts/package.tgz','r:gz') as archive:
    for member in archive.getmembers():
        if member.isfile(): files[member.name]=archive.extractfile(member).read()
# Add the exact committed negative fixture to the existing Editor assembly; no code is executed.
for path in sorted((v.ROOT/'samples/mechanisms/negative-semantic').glob('*.cs')):
    name='Editor/P42Negative/'+path.name
    files[name]=path.read_bytes()
    files[name+'.meta']=('fileFormatVersion: 2\nguid: '+hashlib.sha256(name.encode()).hexdigest()[:32]+'\n').encode()
files['Editor/P42Negative.meta']=b'fileFormatVersion: 2\nguid: b963b0525ebf4dd5b0cfa5cf181e1c36\nfolderAsset: yes\n'
stream=io.BytesIO()
with tarfile.open(fileobj=stream,mode='w',format=tarfile.USTAR_FORMAT) as archive:
    for name,data in sorted(files.items()):
        info=tarfile.TarInfo(name); info.size=len(data);info.mode=0o644;info.mtime=0
        archive.addfile(info,io.BytesIO(data))
package=gzip.compress(stream.getvalue(),mtime=0)
(out/'artifacts/package.tgz').write_bytes(package)
(out/'artifacts/proposal.json').write_bytes((base/'artifacts/proposal.json').read_bytes())
d=json.loads((base/'change-set.json').read_text())
d['id']=d['id'][:-1]+'N'
d['intent']['text']='P4.2 refusal test: pressure-plate with committed semantic-negative source overlay; never admit.'
for artifact in d['artifacts']:
    data=(out/'artifacts'/artifact['name']).read_bytes(); artifact['bytes']=len(data)
    artifact['sha256']=hashlib.sha256(data).hexdigest()
    d['operations'][0]['args'][artifact['role']]['artifact']='sha256:'+artifact['sha256']
(out/'change-set.json').write_text(json.dumps(d,indent=2)+'\n')
(out/'README.md').write_text('# Semantic-negative refusal input\n\nThe pressure-plate candidate envelope contains the exact committed negative-semantic C# sources as an Editor overlay. It is only retained in the candidate CAS for Stage. It must never be admitted. No host confinement or candidate-supplied verdict is used.\n')
(out/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(out))+'\n' for p in sorted(out.rglob('*')) if p.is_file() and p.name!='SHA256SUMS'))
print(out.relative_to(v.ROOT))
