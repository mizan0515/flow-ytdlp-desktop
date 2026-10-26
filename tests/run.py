import functools,http.server,json,pathlib,subprocess,threading,datetime,os
ROOT=pathlib.Path(__file__).resolve().parents[1]
WORK=ROOT/'work'
OUT=pathlib.Path(os.environ.get('FLOW_TEST_OUTPUT',ROOT/'dist/Flow-1.1-win-x64'))
SCRATCH=WORK/'verification'/('core-'+datetime.datetime.now().strftime('%H%M%S'))
SCRATCH.mkdir(parents=True,exist_ok=True)
subprocess.run([str(OUT/'engine/ffmpeg.exe'),'-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc=size=320x180:rate=15','-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','3','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac','-movflags','+faststart',str(SCRATCH/'fixture.mp4')],check=True)
class Handler(http.server.SimpleHTTPRequestHandler):
 def log_message(self,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Handler,directory=str(SCRATCH)))
threading.Thread(target=server.serve_forever,daemon=True).start()
compiler='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
refs=['System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll',str(OUT/'Microsoft.Web.WebView2.Core.dll'),str(OUT/'Microsoft.Web.WebView2.WinForms.dll')]
target=OUT/'CoreTests.exe'
subprocess.run([compiler,'/nologo','/target:exe','/platform:x64','/main:Flow.Tests','/out:'+str(target),*['/r:'+r for r in refs],str(ROOT/'src/App.cs'),str(ROOT/'tests/CoreTests.cs')],check=True)
try:
 subprocess.run([str(target),str(OUT),f'http://127.0.0.1:{server.server_port}/fixture.mp4',str(SCRATCH)],check=True,timeout=240)
 state=json.loads((SCRATCH/'core-data/state.json').read_text(encoding='utf-8'))
 clips=[f for j in state['jobs'] for f in j['files'] if '[0.500-1.500]' in f or '[1.500-2.500]' in f]
 assert len(clips)==2,clips
 for clip in clips:
  measured=float(subprocess.check_output([str(OUT/'engine/ffprobe.exe'),'-v','error','-show_entries','format=duration','-of','default=noprint_wrappers=1:nokey=1',clip],text=True).strip())
  assert abs(measured-1.0)<.15,(clip,measured)
  print('PASS measured precise clip duration',round(measured,3),'seconds',flush=True)
 print('Evidence:',SCRATCH,flush=True)
finally:server.shutdown()
