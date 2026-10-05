"""Read only this packet's containers; redact diagnostics before retaining them."""
import json
from pathlib import Path
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[4] / 'studio/stage'))
from redact import redact
out = Path(__file__).parent / 'diagnostics'
out.mkdir(exist_ok=True)
probe = r'''
import glob,json,os,stat
processes=[]
for path in glob.glob('/proc/[0-9]*/status'):
 try:
  fields=dict(line.rstrip().split(':',1) for line in open(path) if ':' in line)
  if any(name in fields.get('Name','') for name in ('dotnet','Unity','bee','VBCS')):
   command=open(path.replace('/status','/cmdline'),'rb').read().replace(b'\0',b' ').decode(errors='replace')
   processes.append({'pid':fields['Pid'].strip(),'name':fields['Name'].strip(),'threads':fields['Threads'].strip(),'command':command,'environment':{v.split(b'=',1)[0].decode():v.split(b'=',1)[1].decode(errors='replace') for v in open(path.replace('/status','/environ'),'rb').read().split(b'\0') if b'=' in v and v.split(b'=',1)[0] in (b'TMPDIR',b'XDG_RUNTIME_DIR',b'DOTNET_PROCESSOR_COUNT',b'DOTNET_CLI_HOME',b'BEE_BUILD_THREADS')}})
 except (OSError,KeyError): pass
print(json.dumps({'processes':processes,'pidCurrent':open('/sys/fs/cgroup/pids.current').read().strip(),'pidMax':open('/sys/fs/cgroup/pids.max').read().strip(),'runtime':os.environ.get('TMPDIR'),'dotnet':{k:os.environ.get(k) for k in ('DOTNET_PROCESSOR_COUNT','DOTNET_CLI_HOME')},'sockets':[p for p in glob.glob('/tmp/*') + glob.glob(os.environ.get('TMPDIR','/tmp')+'/*') if stat.S_ISSOCK(os.lstat(p).st_mode)]}))
'''
for tick in range(240):
    names = subprocess.run(['docker','ps','--filter','name=gc-stage-','--format','{{.Names}}'],capture_output=True,text=True).stdout.splitlines()
    for name in names:
        inspected = subprocess.run(['docker','inspect',name],capture_output=True,text=True)
        if inspected.returncode: continue
        info=json.loads(inspected.stdout)[0]
        if not any('/gamecore-stage-tmp2-service/' in m.get('Source','') for m in info['Mounts']): continue
        config=info['HostConfig']
        proof={k:config.get(k) for k in ('NetworkMode','ReadonlyRootfs','CapDrop','SecurityOpt','PidsLimit','Tmpfs')}
        (out/(name+'-confinement.json')).write_text(json.dumps(proof,indent=2))
        log=subprocess.run(['docker','exec',name,'/bin/sh','-c','for file in /run/gcs/*.log; do echo FILE:$file; cat "$file"; done'],capture_output=True).stdout
        if log: (out/(name+'-compiler.log')).write_text(redact(log[:8*1024*1024].decode(errors='replace').replace('\x00','')))
        process=subprocess.run(['docker','exec',name,'python3','-c',probe],capture_output=True,text=True)
        if process.returncode == 0:
            with (out/(name+'-process.jsonl')).open('a') as file: file.write(redact(process.stdout))
    time.sleep(10)
