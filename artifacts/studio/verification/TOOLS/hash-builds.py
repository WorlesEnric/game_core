#!/usr/bin/env python3
import hashlib,json,sys
from pathlib import Path
import verify as v
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
for label,folder in [('hollowmere',v.ROOT/'build/HollowmereLinux'),('cleanproof',v.ROOT/'games/cleanproof/Builds/Linux')]:
 rows=[]
 for p in sorted(folder.rglob('*')):
  if not p.is_file() or '_ButDontShipItWithYourGame' in str(p):continue
  h=hashlib.sha256()
  with p.open('rb') as stream:
   while data:=stream.read(1024*1024):h.update(data)
  rows.append((h.hexdigest(),str(p.relative_to(folder)),p.stat().st_size))
 (out/(label+'-player.sha256')).write_text(''.join(h+'  '+name+'\n' for h,name,size in rows))
 (out/(label+'-player.json')).write_text(json.dumps({'utc':v.utc(),'directory':str(folder.relative_to(v.ROOT)),'files':len(rows),'bytes':sum(size for h,name,size in rows),'sourceRevision':v.git('rev-parse','HEAD'),'note':'Binary files remain on the Linux host. This manifest hashes actual built data as well as the launcher.'},indent=2)+'\n')
 print(label,len(rows),'files',sum(size for h,name,size in rows),'bytes')
