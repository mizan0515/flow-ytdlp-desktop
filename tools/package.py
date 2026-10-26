"""Build the public GUI release; official media engines are installed locally."""
import argparse,hashlib,pathlib,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--app-dir',type=pathlib.Path,default=ROOT/'dist/Flow-1.1-win-x64')
parser.add_argument('--output-dir',type=pathlib.Path,default=ROOT/'release')
args=parser.parse_args()
app=args.app_dir.resolve();output=args.output_dir.resolve();output.mkdir(parents=True,exist_ok=True)
required=['Flow.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll']
for name in required:
 if not (app/name).is_file():raise RuntimeError('Missing built file: '+name)
target=output/'Flow-1.1-Windows.zip'
with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
 for name in required:archive.write(app/name,'Flow/'+name)
 for directory in ['ui','licenses']:
  for file in (app/directory).rglob('*'):
   if file.is_file():archive.write(file,'Flow/'+file.relative_to(app).as_posix())
 for name in ['Setup-Engine.cmd','Setup-Engine.ps1','README.md','THIRD-PARTY.md']:
  archive.write(ROOT/name,'Flow/'+name)
with zipfile.ZipFile(target) as archive:
 if archive.testzip():raise RuntimeError('ZIP failed CRC verification')
 names=archive.namelist()
 assert not any('/engine/' in n or '/data/' in n or '/work/' in n or '/Downloads/' in n for n in names)
digest=hashlib.sha256(target.read_bytes()).hexdigest()
(output/'SHA256SUMS.txt').write_text(digest+'  '+target.name+'\n',encoding='utf-8')
print(target, target.stat().st_size, 'bytes',digest)
