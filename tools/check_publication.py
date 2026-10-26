"""Check the exact Git-tracked source manifest without inspecting private paths."""
import json,pathlib,re,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
files=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode().split('\0')
problems=[];entries=[]
for relative in filter(None,files):
 p=pathlib.PurePosixPath(relative)
 if any(part.lower() in ['data','downloads','webview2','secrets','node_modules','__pycache__'] for part in p.parts) or p.name.startswith('.env') or p.suffix.lower() in ['.exe','.dll','.zip','.key','.pfx','.pem','.pyc','.log']:
  problems.append('Forbidden tracked file: '+relative);continue
 data=(ROOT/relative).read_bytes()
 if b'\x00' not in data and p.suffix.lower() not in ['.png','.jpg','.jpeg','.ico']:
  text=data.decode('utf-8-sig')
  for pattern in [r'(?:ghp_|github_pat_|sk-proj-)[A-Za-z0-9_]{16,}',r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',r'''[A-Z]:[\\/]Users[\\/][^\s"']+''']:
   if re.search(pattern,text):problems.append('Secret or personal host path in '+relative)
 entries.append({'path':relative,'bytes':len(data)})
if problems:
 print('\n'.join(problems));sys.exit(1)
print(json.dumps({'status':'PASS','files':len(entries),'bytes':sum(e['bytes'] for e in entries),'manifest':entries},indent=2))
