"""Read cgroup counters from the host without consuming a container PID."""
import json
from pathlib import Path
import subprocess
import time
out=Path(__file__).parent/'diagnostics/pid-counters.jsonl'
for _ in range(1800):
    names=subprocess.run(['docker','ps','--filter','name=gc-stage-','--format','{{.Names}}'],capture_output=True,text=True).stdout.splitlines()
    for name in names:
        try:
            info=json.loads(subprocess.check_output(['docker','inspect',name],stderr=subprocess.DEVNULL))[0]
            if not any('/gamecore-stage-tmp2-service/' in m.get('Source','') for m in info['Mounts']): continue
            rel=Path(f"/proc/{info['State']['Pid']}/cgroup").read_text().strip().split(':')[-1]
            root=Path('/sys/fs/cgroup')/rel.lstrip('/')
            data={n:(root/n).read_text().strip() for n in ('pids.current','pids.max','pids.events','pids.peak') if (root/n).exists()}
            with out.open('a') as f: f.write(json.dumps({'container':name,'at':time.time(),**data})+'\n')
        except (OSError,subprocess.CalledProcessError): pass
    time.sleep(1)
