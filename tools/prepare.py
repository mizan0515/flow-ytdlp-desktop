"""Fetch pinned official dependencies into this workspace, never install globally."""
import argparse, concurrent.futures, hashlib, json, pathlib, tarfile, urllib.request, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
DEPS = ROOT / 'work/deps'
OUT = ROOT / 'dist/Flow-1.1-win-x64'
DEPS.mkdir(parents=True,exist_ok=True)
(OUT / 'engine').mkdir(parents=True, exist_ok=True)
URLS = {
 'yt-dlp.exe': ('https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp.exe', '33121d192d706e62f684a06f80918e3f3a8ba52ac3a65213b6951c39c7d9f555'),
 'yt-dlp.tar.gz': ('https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp.tar.gz', '5f1e419f76e49c996061511ef9eacce50d4aa4959c34480539562e9e1295708d'),
 'ffmpeg.zip': ('https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip', '7114ac0cc86f2cb625f49bb0e5f39ba81bd82ff8b239ced2c1f8c36cb29f3720'),
 'deno.zip': ('https://github.com/denoland/deno/releases/download/v2.9.8/deno-x86_64-pc-windows-msvc.zip', 'cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf'),
 'webview.nupkg': ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4314.35/microsoft.web.webview2.1.0.4314.35.nupkg', '90778deb1b77e36292828b4b5833c9255c27533f40e0ddd55891ae718944edc4'),
}
def fetch(item):
 name, (url, digest) = item
 target = DEPS / name
 if not target.exists():
  with urllib.request.urlopen(url, timeout=90) as response, target.with_suffix(target.suffix+'.part').open('wb') as stream:
   while block := response.read(1024*1024): stream.write(block)
  target.with_suffix(target.suffix+'.part').replace(target)
 actual = hashlib.sha256(target.read_bytes()).hexdigest()
 if digest and actual != digest: raise RuntimeError('SHA256 mismatch: '+name)
 print(name, target.stat().st_size, actual, flush=True)
 return dict(name=name, url=url, sha256=actual)

if __name__ == '__main__':
 parser=argparse.ArgumentParser()
 parser.add_argument('--sdk-only',action='store_true',help='Prepare only the WebView2 build references')
 options=parser.parse_args()
 if not options.sdk_only:
  # FFmpeg latest is a moving asset: obtain its current official digest instead
  # of accepting a changed archive against an obsolete build checksum.
  with urllib.request.urlopen('https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest',timeout=60) as response:
   release=json.load(response)
  asset=next(a for a in release['assets'] if a['name']=='ffmpeg-master-latest-win64-gpl-shared.zip')
  digest=asset.get('digest','')
  if not digest.startswith('sha256:'):raise RuntimeError('Official FFmpeg asset has no SHA256 digest')
  URLS['ffmpeg.zip']=(asset['browser_download_url'],digest.removeprefix('sha256:'))
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
  manifest = list(pool.map(fetch, [(k,v) for k,v in URLS.items() if not options.sdk_only or k=='webview.nupkg']))
 import shutil
 with zipfile.ZipFile(DEPS/'webview.nupkg') as archive:
  archive.extractall(DEPS/'webview')
  for name in ['Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll']:
   (OUT/name).write_bytes(archive.read('lib/net462/'+name))
  (OUT/'WebView2Loader.dll').write_bytes(archive.read('runtimes/win-x64/native/WebView2Loader.dll'))
  for name in ['LICENSE.txt','NOTICE.txt']:
   target=OUT/'licenses/webview2'/name
   target.parent.mkdir(parents=True,exist_ok=True)
   target.write_bytes(archive.read(name))
 if options.sdk_only:
  print('Build SDK ready:',OUT)
  raise SystemExit(0)
 shutil.copy2(DEPS/'yt-dlp.exe', OUT/'engine/yt-dlp.exe')
 with tarfile.open(DEPS/'yt-dlp.tar.gz') as archive:
  archive.extractall(DEPS/'ytdlp-source', filter='data')
 with zipfile.ZipFile(DEPS/'ffmpeg.zip') as archive:
  for name in archive.namelist():
   p = pathlib.PurePosixPath(name)
   if '/bin/' in name and (p.suffix=='.dll' or p.name in ['ffmpeg.exe','ffprobe.exe']):
    (OUT/'engine'/p.name).write_bytes(archive.read(name))
   elif 'LICENSE' in p.name.upper() or p.name.lower() in ['version.txt','readme.txt']:
    dest=OUT/'licenses'/'ffmpeg'/p.name
    dest.parent.mkdir(parents=True,exist_ok=True)
    dest.write_bytes(archive.read(name))
 with zipfile.ZipFile(DEPS/'deno.zip') as archive:
  (OUT/'engine/deno.exe').write_bytes(archive.read('deno.exe'))
 (OUT/'dependency-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
 print('Dependencies ready:', OUT, flush=True)
