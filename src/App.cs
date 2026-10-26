using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Flow {
public class Setting { public string flag; public List<string> values = new List<string>(); }
public class JobRequest {
 public List<string> urls = new List<string>(); public List<Setting> settings = new List<Setting>();
 public string profile="best", folder="", subtitle="", mode="download", stdinText=""; public bool allowRisk=false;
}
public class Job {
 public string id, title, status, stage, created, folder, mode, error="", output="", version;
 public double progress; public bool sensitive, requiresInput, cancelled;
 public string stdinText=""; public List<string> urls=new List<string>(), args=new List<string>(), files=new List<string>(), log=new List<string>();
}
public class Preset { public string id, name, profile, subtitle; public List<Setting> settings=new List<Setting>(); }
public class State {
 public int schema=1, concurrency=2; public string folder="", theme="light";
 public List<Job> jobs=new List<Job>(); public List<Preset> presets=new List<Preset>();
}
public class Result { public int code; public string stdout="", stderr=""; public bool timedOut; }
public class EngineOption { public string flag; public string[] flags; public bool takesValue, sensitive, risk; public int nargs; }
public class Catalog { public string version; public List<EngineOption> options; }

public sealed class ProcessGroup : IDisposable {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a,string name);
 [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr j,int c,IntPtr info,uint size);
 [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr j,IntPtr p);
 [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr j,uint code);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] struct Basic { public long perProcess,perJob; public uint flags; public UIntPtr min,max; public uint active; public UIntPtr affinity; public uint priority,scheduling; }
 [StructLayout(LayoutKind.Sequential)] struct Io { public ulong a,b,c,d,e,f; }
 [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic basic; public Io io; public UIntPtr a,b,c,d; }
 IntPtr handle; Process process;
 public ProcessGroup(Process p) {
  process=p; handle=CreateJobObject(IntPtr.Zero,null);
  Extended info=new Extended(); info.basic.flags=0x2000;
  IntPtr ptr=Marshal.AllocHGlobal(Marshal.SizeOf(info));
  try { Marshal.StructureToPtr(info,ptr,false); if(handle==IntPtr.Zero || !SetInformationJobObject(handle,9,ptr,(uint)Marshal.SizeOf(info)) || !AssignProcessToJobObject(handle,p.Handle)) { if(handle!=IntPtr.Zero) CloseHandle(handle); handle=IntPtr.Zero; try {p.Kill();} catch {} throw new Exception("작업 프로세스를 안전하게 관리할 수 없습니다."); } }
  finally { Marshal.FreeHGlobal(ptr); }
 }
 public void Kill() { if(handle!=IntPtr.Zero) TerminateJobObject(handle,130); else try {if(!process.HasExited)process.Kill();}catch{} }
 public void Dispose() { if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;} }
}

public class Backend : IDisposable {
 public readonly string Root, DataDir, EngineDir; public State state; public Catalog catalog;
 readonly object gate=new object(); readonly Dictionary<string,ProcessGroup> running=new Dictionary<string,ProcessGroup>();
 readonly Action<object> notify; bool closing; public string engineVersion="";
 public static JavaScriptSerializer Json() {return new JavaScriptSerializer {MaxJsonLength=32*1024*1024,RecursionLimit=100};}
 public Backend(string root, Action<object> sink, string dataDir=null) {
  Root=root; EngineDir=Path.Combine(root,"engine"); DataDir=dataDir??Path.Combine(root,"data");notify=sink;
  Directory.CreateDirectory(DataDir);
  catalog=Json().Deserialize<Catalog>(File.ReadAllText(Path.Combine(root,"ui","catalog.json")));
  try {state=Json().Deserialize<State>(File.ReadAllText(Path.Combine(DataDir,"state.json")));} catch {state=new State();}
  if(state==null)state=new State();
  if(String.IsNullOrEmpty(state.folder))state.folder=Path.Combine(root,"Downloads");
  foreach(var j in state.jobs) if(j.status=="running"||j.status=="queued") {j.status="interrupted";j.stage="앱 종료로 중단됨";}
  Save();
 }
 public object Snapshot() {lock(gate)return Json().DeserializeObject(Json().Serialize(new {state=state,engineVersion=engineVersion,catalogVersion=catalog.version,engineReady=File.Exists(Path.Combine(EngineDir,"yt-dlp.exe")),root=Root}));}
 void Emit() {if(!closing)notify(new {type="state",data=Snapshot()});}
 public static string Quote(string value) {
  if(value==null)value="";
  return "\""+Regex.Replace(value,@"(\\*)\""",m=>new string('\\',m.Groups[1].Length*2+1)+"\"").TrimEnd('\\')+new string('\\',value.Reverse().TakeWhile(c=>c=='\\').Count()*2)+"\"";
 }
 public static string Redact(string s) {
  if(s==null)return "";
  s=Regex.Replace(s,@"(?i)(https?://)[^\s/@]+:[^\s/@]+@","$1[redacted]@");
  s=Regex.Replace(s,@"(?i)([?&](?:token|key|auth|authorization|password|signature|sig|access_token|po_token)=)[^&\s]+","$1[redacted]");
  s=Regex.Replace(s,@"(?i)((?:authorization|cookie|set-cookie|password|po_token)\s*[:=]\s*)[^\r\n]+","$1[redacted]");
  return s;
 }
 void Save() {
  lock(gate) {
   var copy=Json().Deserialize<State>(Json().Serialize(state));
   foreach(var j in copy.jobs) {if(!String.IsNullOrEmpty(j.stdinText)){j.stdinText="";j.requiresInput=true;}if(j.sensitive) {j.args.Clear();j.urls.Clear();j.log.Clear();j.output="";j.title="인증 정보가 포함된 작업";j.error=String.IsNullOrEmpty(j.error)?"":"인증 작업의 오류 내용은 저장하지 않습니다.";j.requiresInput=true;}}
   string path=Path.Combine(DataDir,"state.json"),tmp=path+".tmp";
   File.WriteAllText(tmp,Json().Serialize(copy),new UTF8Encoding(false));
   if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
  }
 }
 public async Task<Result> Run(List<string> args, Action<string> line=null, string id=null,int timeout=0,string stdinText="",string binaryTarget=null) {
  var psi=new ProcessStartInfo(Path.Combine(EngineDir,"yt-dlp.exe"),String.Join(" ",args.Select(Quote))) {WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  psi.EnvironmentVariables["PYTHONUTF8"]="1";
  // This only affects our child process. Never load host-global plugin directories.
  psi.EnvironmentVariables["YTDLP_NO_PLUGINS"]="1";
  if(args.Contains("--plugin-dirs")||args.Contains("--config-locations"))psi.EnvironmentVariables.Remove("YTDLP_NO_PLUGINS");
  var result=new Result(); var output=new StringBuilder();var errors=new StringBuilder();object bufferGate=new object();
  using(var p=new Process {StartInfo=psi}) {
   p.OutputDataReceived+=(s,e)=> {if(e.Data!=null) {lock(bufferGate) {if(output.Length<12*1024*1024)output.AppendLine(e.Data);} if(line!=null)line(e.Data);}};
   p.ErrorDataReceived+=(s,e)=> {if(e.Data!=null) {lock(bufferGate) {if(errors.Length<2*1024*1024)errors.AppendLine(e.Data);} if(line!=null)line(e.Data);}};
   p.Start();
   using(var group=new ProcessGroup(p)) {
    if(id!=null)lock(gate) {running[id]=group;var job=state.jobs.FirstOrDefault(j=>j.id==id);if(job!=null&&job.cancelled)group.Kill();}
    Task binaryCopy=null;
    if(binaryTarget==null)p.BeginOutputReadLine();else binaryCopy=CopyBinary(p,binaryTarget);
    p.BeginErrorReadLine();
    Task inputWrite=WriteInput(p,stdinText);
    bool exited=await Task.Run(()=>timeout==0 ? Wait(p) : p.WaitForExit(timeout));
    if(!exited){result.timedOut=true;group.Kill();}
    await Task.Run(()=>p.WaitForExit()); if(binaryCopy!=null)await binaryCopy;await inputWrite;result.code=p.ExitCode;
    if(id!=null)lock(gate)running.Remove(id);
   }
  }
  result.stdout=output.ToString();result.stderr=errors.ToString();return result;
 }
 static async Task WriteInput(Process p,string text) {try{if(!String.IsNullOrEmpty(text)){var bytes=Encoding.UTF8.GetBytes(text);await p.StandardInput.BaseStream.WriteAsync(bytes,0,bytes.Length);}}catch(IOException){}finally{try{p.StandardInput.Close();}catch{}}}
 static async Task CopyBinary(Process p,string path){using(var f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read)){await p.StandardOutput.BaseStream.CopyToAsync(f);}}
 static bool Wait(Process p) {p.WaitForExit();return true;}
 public List<string> BaseArgs() {return new List<string>{"--ignore-config","--no-plugin-dirs","--color","never","--encoding","utf-8","--ffmpeg-location",EngineDir,"--js-runtimes","deno:"+Path.Combine(EngineDir,"deno.exe"),"--cache-dir",Path.Combine(DataDir,"cache")};}
 public async Task CheckVersion() {var r=await Run(new List<string>{"--version"},null,null,15000);engineVersion=r.stdout.Trim();Emit();}
 void Validate(JobRequest r) {
  if(r.urls==null)r.urls=new List<string>();if(r.settings==null)r.settings=new List<Setting>();
  if(r.urls.Count>2000 || r.settings.Count>1500 || (r.stdinText??"").Length>1024*1024)throw new Exception("한 작업의 입력 수가 너무 많습니다.");
  foreach(var u in r.urls)if(String.IsNullOrWhiteSpace(u)||u.IndexOf('\0')>=0||u.Length>16000||u.StartsWith("-"))throw new Exception("올바른 URL 또는 검색 입력을 넣어 주세요.");
  foreach(var s in r.settings) {
   var o=catalog.options.FirstOrDefault(x=>x.flags.Contains(s.flag));
   if(o==null)throw new Exception("지원 목록에 없는 옵션: "+s.flag);
   if(s.values==null)s.values=new List<string>();
   if((o.takesValue && s.values.Count!=o.nargs)||(!o.takesValue&&s.values.Count!=0))throw new Exception(s.flag+" 값의 개수를 확인해 주세요.");
   if(s.values.Any(v=>v==null||v.Contains("\0")||v.Length>64000))throw new Exception("옵션 값이 올바르지 않습니다.");
   if(o.risk&&!r.allowRisk)throw new Exception("외부 실행·파일 변경 옵션을 확인한 후 실행해 주세요.");
  }
  if(r.mode!="command"&&r.mode!="download")throw new Exception("잘못된 실행 모드입니다.");
  if(r.mode=="download"&&r.urls.Count==0&&!r.settings.Any(s=>s.flag=="--batch-file"||s.flag=="--load-info-json"))throw new Exception("다운로드할 링크를 넣어 주세요.");
 }
 public List<string> BuildArgs(JobRequest r) {
  Validate(r);var a=BaseArgs();
  if(r.mode=="download") {
   bool sections=r.settings.Any(s=>s.flag=="--download-sections");
   string template=sections?"%(title).140B [%(id)s] [%(section_start).3f-%(section_end).3f].%(ext)s":"%(title).180B [%(id)s].%(ext)s";
   a.AddRange(new[]{"--newline","--progress","--progress-delta","0.25","--no-overwrites","-P",r.folder,"-o",template});
   switch(r.profile) {
    case "compatible":a.AddRange(new[]{"-f","bv*[vcodec^=avc1]+ba[acodec^=mp4a]/b[ext=mp4]/b","--merge-output-format","mp4"});break;
    case "audio":a.AddRange(new[]{"-f","bestaudio/best","-x"});break;
    case "mp3":a.AddRange(new[]{"-f","bestaudio/best","-x","--audio-format","mp3","--audio-quality","0"});break;
    case "archive":a.AddRange(new[]{"--write-info-json","--write-description","--write-thumbnail","--write-subs"});break;
   }
   if(!String.IsNullOrWhiteSpace(r.subtitle))a.AddRange(new[]{"--write-subs","--sub-langs",r.subtitle});
  }
  // Settings are ordered occurrences; duplicates and negative/reset options retain CLI semantics.
  foreach(var s in r.settings) {a.Add(s.flag);a.AddRange(s.values);}
  if(r.mode=="download") {
   if(r.settings.Any(s=>s.flag=="--quiet"||s.flag=="-q"||s.flag=="--no-progress"))throw new Exception("조용한 출력 옵션은 ‘도구 실행’에서 사용할 수 있습니다. 다운로드 화면에는 진행 정보가 필요합니다.");
   // Use stderr for controlled progress. User --print/JSON output cannot corrupt the event stream.
   a.AddRange(new[]{"--progress-template","download:FLOW_PROGRESS:%(progress)j","--progress-template","postprocess:FLOW_POST:%(progress)j","--print","after_move:FLOW_FILE:%(filepath)j","--no-simulate"});
  }
  a.Add("--"); a.AddRange(r.urls);return a;
 }
 public object PreviewArgs(JobRequest r) {r.folder=String.IsNullOrWhiteSpace(r.folder)?state.folder:r.folder;var a=BuildArgs(r); bool sensitive=r.settings.Any(s=>catalog.options.Any(o=>o.flags.Contains(s.flag)&&o.sensitive));return new {args=sensitive?new[]{"인증 정보가 포함되어 인수를 숨겼습니다."}:a.ToArray(),risk=r.settings.Where(s=>catalog.options.Any(o=>o.flags.Contains(s.flag)&&o.risk)).Select(s=>s.flag).ToArray()};}
 public Job Submit(JobRequest r) {
  r.folder=String.IsNullOrWhiteSpace(r.folder)?state.folder:r.folder;
  r.folder=Path.GetFullPath(r.folder);var args=BuildArgs(r);
  if(r.mode=="download")Directory.CreateDirectory(r.folder);
  var j=new Job {id=Guid.NewGuid().ToString("N"),title=r.urls.Count>0?Redact(r.urls[0]):"엔진 도구 실행",urls=r.urls.ToList(),args=args,folder=r.folder,mode=r.mode,status="queued",stage="대기 중",version=engineVersion,created=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),stdinText=r.stdinText??"",sensitive=r.settings.Any(s=>catalog.options.Any(o=>o.flags.Contains(s.flag)&&o.sensitive))||r.urls.Any(u=>Redact(u)!=u)||!String.IsNullOrEmpty(r.stdinText)};
  lock(gate){if(state.jobs.Count>=2000)throw new Exception("기록이 2,000개입니다. 오래된 기록을 정리해 주세요.");state.jobs.Insert(0,j);state.folder=r.folder;Save();}Emit();Pump();return j;
 }
 void Pump() {
  lock(gate) {
   if(closing)return;
   int active=state.jobs.Count(j=>j.status=="running");
   foreach(var j in state.jobs.Where(j=>j.status=="queued").OrderBy(j=>j.created).Take(Math.Max(0,state.concurrency-active)).ToArray()) {
    j.status="running";j.stage="정보 확인 중";Save();Task.Run(()=>Execute(j));
   }
  }Emit();
 }
 async Task Execute(Job j) {
  DateTime last=DateTime.MinValue;
  try {
   bool binary=j.mode=="command"&&j.args.Select((v,i)=>new{v,i}).Any(x=>(x.v=="-o"||x.v=="--output")&&x.i+1<j.args.Count&&j.args[x.i+1]=="-");
   string binaryPath=binary?Path.Combine(j.folder,"Flow-stream-"+j.id.Substring(0,8)+".bin"):null;
   if(binary)Directory.CreateDirectory(j.folder);
   var r=await Run(j.args,line=> {
    lock(gate) {
     try {
      if(line.StartsWith("FLOW_PROGRESS:")) {var p=Json().Deserialize<Dictionary<string,object>>(line.Substring(14));double total=Number(p,"total_bytes");if(total<=0)total=Number(p,"total_bytes_estimate");j.progress=total>0?Math.Min(99,Number(p,"downloaded_bytes")/total*100):-1;j.stage="다운로드 중";if(p.ContainsKey("speed"))j.stage+=" · "+Size(Number(p,"speed"))+"/s";}
      else if(line.StartsWith("FLOW_POST:")){j.stage="병합·변환 중";j.progress=-1;}
      else if(line.StartsWith("FLOW_FILE:")){string file=Json().Deserialize<string>(line.Substring(10));if(File.Exists(file)&&!j.files.Contains(file))j.files.Add(file);}
      else {string safe=j.sensitive?"인증 작업: 상세 로그를 숨겼습니다.":Redact(line);j.log.Add(safe.Length>8000?safe.Substring(0,8000):safe);if(j.log.Count>350)j.log.RemoveAt(0);}
     }catch {j.stage="처리 중";}
    }
    if((DateTime.UtcNow-last).TotalMilliseconds>250){last=DateTime.UtcNow;Emit();}
   },j.id,0,j.stdinText,binaryPath);
   lock(gate) {
    j.output=j.sensitive?"인증 작업의 원시 출력은 표시하지 않습니다.":Redact(r.stdout.Length>300000?r.stdout.Substring(r.stdout.Length-300000):r.stdout);
    if(j.cancelled){j.status="cancelled";j.stage="중단됨 · 재시도 가능";}
    else if(r.code!=0){j.status="failed";j.stage="실패";j.error=j.sensitive?"인증 작업이 실패했습니다. 인증 설정을 확인해 주세요.":Redact(r.stderr.Length>6000?r.stderr.Substring(r.stderr.Length-6000):r.stderr);if(String.IsNullOrWhiteSpace(j.error))j.error="엔진 종료 코드: "+r.code;}
    else if(j.mode=="command"){j.status="completed";j.stage="도구 실행 완료";j.progress=100;if(binaryPath!=null&&File.Exists(binaryPath))j.files.Add(binaryPath);}
    else if(j.log.Any(l=>l.StartsWith("ERROR:"))){j.status="failed";j.stage="일부 항목 처리 실패";j.error="오류가 발생했습니다. 일부 파일은 저장되었을 수 있습니다. 로그를 확인해 주세요.";}
    else if(j.files.Count>0){j.status="completed";j.stage="완료";j.progress=100;}
    else {j.status="skipped";j.stage="저장된 새 파일 없음";j.error="중복·필터로 생략되었거나 조회 전용 옵션이 적용되었습니다. 상세 로그를 확인해 주세요.";}
    Save();
   }
  }catch(Exception e){lock(gate){j.status=j.cancelled?"cancelled":"failed";j.stage=j.cancelled?"중단됨":"실행 실패";j.error=Redact(e.Message);Save();}}
  Emit();Pump();
 }
 static double Number(Dictionary<string,object> d,string k) {double v;return d.ContainsKey(k)&&Double.TryParse(Convert.ToString(d[k],System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out v)?v:0;}
 static string Size(double n) {return n>1048576?(n/1048576).ToString("0.0")+" MB":(n/1024).ToString("0")+" KB";}
 public void Stop(string id) {lock(gate){var j=Find(id);if(j.status!="queued"&&j.status!="running")return;j.cancelled=true;if(running.ContainsKey(id))running[id].Kill();if(j.status=="queued"){j.status="cancelled";j.stage="대기 취소";}Save();}Emit();}
 public Job Find(string id) {var j=state.jobs.FirstOrDefault(x=>x.id==id);if(j==null)throw new Exception("작업을 찾을 수 없습니다.");return j;}
 public void Retry(string id) {lock(gate){var j=Find(id);if(j.status=="running"||j.status=="queued")throw new Exception("진행 중인 작업입니다.");if(j.requiresInput||j.args.Count==0)throw new Exception("인증 정보는 디스크에 저장하지 않습니다. 새 다운로드에서 다시 설정해 주세요.");j.cancelled=false;j.status="queued";j.stage="재시도 대기";j.error="";j.progress=0;j.files.Clear();j.log.Clear();Save();}Emit();Pump();}
 public void Remove(string id) {lock(gate){var j=Find(id);if(j.status=="running")throw new Exception("먼저 작업을 중단해 주세요.");state.jobs.Remove(j);Save();}Emit();}
 public async Task<object> Analyze(JobRequest request) {
  Validate(request);var args=BaseArgs();
  var safe=new HashSet<string>{"--proxy","--socket-timeout","--cookies","--cookies-from-browser","--username","--password","--video-password","--extractor-args","--add-headers","--force-ipv4","--force-ipv6","--impersonate","--default-search"};
  foreach(var s in request.settings.Where(s=>safe.Contains(s.flag))){args.Add(s.flag);args.AddRange(s.values);}
  args.AddRange(new[]{"--dump-single-json","--flat-playlist","--playlist-end","50","--skip-download","--no-warnings","--"});args.Add(request.urls.First());
  var r=await Run(args,null,null,60000);if(r.code!=0)throw new Exception(Redact(r.stderr));
  var info=Json().Deserialize<Dictionary<string,object>>(r.stdout);
  // Direct media extractors may not know duration. Probe only an HTTP(S) media
  // URL explicitly returned by the engine, with a short timeout and no file protocols.
  if((!info.ContainsKey("duration")||Number(info,"duration")<=0)&&!info.ContainsKey("entries")&&info.ContainsKey("url")) {
   double duration=await ProbeDuration(Convert.ToString(info["url"]));
   if(duration>0)info["duration"]=duration;
  }
  return new {info=info,note="재생목록 미리보기는 최대 50개입니다. 실제 다운로드의 선택 범위는 전체 옵션 설정을 따릅니다."};
 }
 public async Task<double> ProbeDuration(string mediaUrl) {
  Uri uri;if(!Uri.TryCreate(mediaUrl,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https"))return 0;
  try {
   var args=new[]{"-v","error","-protocol_whitelist","http,https,tcp,tls,crypto","-rw_timeout","10000000","-show_entries","format=duration","-of","default=noprint_wrappers=1:nokey=1","-i",mediaUrl};
   using(var p=new Process{StartInfo=new ProcessStartInfo(Path.Combine(EngineDir,"ffprobe.exe"),String.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8}}) {
    p.Start();using(var group=new ProcessGroup(p)) {
     var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
     if(!await Task.Run(()=>p.WaitForExit(15000)))group.Kill();
     await Task.Run(()=>p.WaitForExit());string text=await output;await errors;
     double value;if(p.ExitCode==0&&Double.TryParse(text.Trim(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>0&&!Double.IsInfinity(value)&&!Double.IsNaN(value))return value;
    }
   }
  }catch{}return 0;
 }
 public void SavePreset(Preset p) {if(String.IsNullOrWhiteSpace(p.name))throw new Exception("프리셋 이름을 입력해 주세요.");p.name=p.name.Trim().Substring(0,Math.Min(60,p.name.Trim().Length));p.id=Guid.NewGuid().ToString("N");p.settings=p.settings.Where(s=>!catalog.options.Any(o=>o.flags.Contains(s.flag)&&o.sensitive)).ToList();Validate(new JobRequest{mode="command",settings=p.settings,allowRisk=true});lock(gate){if(state.presets.Count>=200)throw new Exception("프리셋 최대 200개");state.presets.Add(p);Save();}Emit();}
 public void DeletePreset(string id){lock(gate){state.presets.RemoveAll(p=>p.id==id);Save();}Emit();}
 public void Preferences(string folder,string theme,int concurrency) {lock(gate){if(!String.IsNullOrWhiteSpace(folder))state.folder=Path.GetFullPath(folder);state.theme=theme=="dark"?"dark":"light";state.concurrency=Math.Max(1,Math.Min(4,concurrency));Save();}Emit();Pump();}
 public void Dispose(){lock(gate){closing=true;foreach(var j in state.jobs.Where(j=>j.status=="running")){j.cancelled=true;j.status="interrupted";j.stage="앱 종료로 중단됨";}foreach(var g in running.Values)g.Kill();Save();}}
}

public class MainWindow : Form {
 readonly WebView2 web=new WebView2(); Backend backend; string root=AppDomain.CurrentDomain.BaseDirectory; bool ready;
 public MainWindow() {
  Text="Flow · yt-dlp desktop"; Width=1240;Height=860;MinimumSize=new System.Drawing.Size(960,680);StartPosition=FormStartPosition.CenterScreen;BackColor=System.Drawing.Color.FromArgb(247,248,252);
  Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);web.Dock=DockStyle.Fill;Controls.Add(web);
  string[] launchArgs=Environment.GetCommandLineArgs();int testIndex=Array.IndexOf(launchArgs,"--test-data");
  backend=new Backend(root,Send,testIndex>=0&&testIndex+1<launchArgs.Length?Path.GetFullPath(launchArgs[testIndex+1]):null);Load+=async(s,e)=>await Initialize();FormClosing+=OnClosing;
 }
 async Task Initialize() {
  try {
   CoreWebView2EnvironmentOptions envOptions=null;
   string[] args=Environment.GetCommandLineArgs();int inspectAt=Array.IndexOf(args,"--inspect-port");int port;
   if(inspectAt>=0&&inspectAt+1<args.Length&&Int32.TryParse(args[inspectAt+1],out port)&&port>=1024&&port<=65535)envOptions=new CoreWebView2EnvironmentOptions("--remote-debugging-port="+port);
   else if(args.Contains("--inspect"))envOptions=new CoreWebView2EnvironmentOptions("--remote-debugging-port=50101");
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(backend.DataDir,"WebView2"),envOptions);
   await web.EnsureCoreWebView2Async(env);
   web.CoreWebView2.SetVirtualHostNameToFolderMapping("flow.local",Path.Combine(root,"ui"),CoreWebView2HostResourceAccessKind.DenyCors);
   web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;
   web.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;web.CoreWebView2.Settings.IsGeneralAutofillEnabled=false;
   web.CoreWebView2.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
   web.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
   web.CoreWebView2.NavigationStarting+=(s,e)=>{Uri u;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out u)||u.Scheme!="https"||u.Host!="flow.local")e.Cancel=true;};
   web.CoreWebView2.WebMessageReceived+=Message;ready=true;web.CoreWebView2.Navigate("https://flow.local/index.html");
   await backend.CheckVersion();
  }catch(Exception e){MessageBox.Show("앱을 시작할 수 없습니다. Microsoft Edge WebView2 Runtime이 필요합니다.\n\n"+e.Message,"Flow",MessageBoxButtons.OK,MessageBoxIcon.Error);Close();}
 }
 void Send(object value) {if(IsDisposed||!ready)return;if(InvokeRequired){try{BeginInvoke(new Action(()=>Send(value)));}catch{}return;}try{web.CoreWebView2.PostWebMessageAsJson(Backend.Json().Serialize(value));}catch{}}
 async void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
  string id="";
  try {
   Uri origin=new Uri(e.Source);if(origin.Scheme!="https"||origin.Host!="flow.local")return;
   var m=Backend.Json().Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);id=Convert.ToString(m["id"]);string action=Convert.ToString(m["action"]);string data=Backend.Json().Serialize(m.ContainsKey("data")?m["data"]:new{});object result=null;
   switch(action) {
    case "init":result=backend.Snapshot();break;
    case "analyze":result=await backend.Analyze(Backend.Json().Deserialize<JobRequest>(data));break;
    case "preview":result=backend.PreviewArgs(Backend.Json().Deserialize<JobRequest>(data));break;
    case "queue":result=backend.Submit(Backend.Json().Deserialize<JobRequest>(data));break;
    case "stop":backend.Stop(Get(data,"id"));break;
    case "retry":backend.Retry(Get(data,"id"));break;
    case "remove":backend.Remove(Get(data,"id"));break;
    case "savePreset":backend.SavePreset(Backend.Json().Deserialize<Preset>(data));break;
    case "deletePreset":backend.DeletePreset(Get(data,"id"));break;
    case "preferences":var prefs=Backend.Json().Deserialize<Dictionary<string,object>>(data);backend.Preferences(Convert.ToString(prefs["folder"]),Convert.ToString(prefs["theme"]),Convert.ToInt32(prefs["concurrency"]));break;
    case "browseFolder":using(var dialog=new FolderBrowserDialog{Description="다운로드할 폴더 선택",SelectedPath=backend.state.folder})if(dialog.ShowDialog(this)==DialogResult.OK)result=dialog.SelectedPath;break;
    case "browseFile":using(var file=new OpenFileDialog{Title="옵션에 사용할 파일 선택",CheckFileExists=true})if(file.ShowDialog(this)==DialogResult.OK)result=file.FileName;break;
    case "readBatch":using(var batch=new OpenFileDialog{Title="URL 목록 가져오기",Filter="텍스트 파일|*.txt;*.list|모든 파일|*.*"})if(batch.ShowDialog(this)==DialogResult.OK){if(new FileInfo(batch.FileName).Length>1024*1024)throw new Exception("목록 파일은 1MB 이하여야 합니다.");result=File.ReadAllLines(batch.FileName).Where(l=>!String.IsNullOrWhiteSpace(l)&&!"#;]".Contains(l.Trim()[0])).ToArray();}break;
    case "openFolder":var folder=Get(data,"path");if(!Directory.Exists(folder))throw new Exception("폴더를 찾을 수 없습니다.");Process.Start(new ProcessStartInfo("explorer.exe",Backend.Quote(Path.GetFullPath(folder))){UseShellExecute=true});break;
    case "openFile":var job=backend.Find(Get(data,"id"));if(job.files.Count==0||!File.Exists(job.files[0]))throw new Exception("파일을 찾을 수 없습니다.");string path=job.files[0];if(!new[]{".mp4",".mkv",".webm",".mp3",".m4a",".opus",".wav",".flac",".ogg",".mov",".avi",".aac"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new Exception("이 형식은 폴더에서 직접 열어 주세요.");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});break;
    case "exportPresets":using(var save=new SaveFileDialog{Title="프리셋 내보내기",FileName="Flow-presets.json",Filter="JSON|*.json"})if(save.ShowDialog(this)==DialogResult.OK){File.WriteAllText(save.FileName,Backend.Json().Serialize(backend.state.presets),new UTF8Encoding(false));result=true;}break;
    case "importPresets":using(var load=new OpenFileDialog{Title="프리셋 가져오기",Filter="JSON|*.json"})if(load.ShowDialog(this)==DialogResult.OK){if(new FileInfo(load.FileName).Length>2*1024*1024)throw new Exception("프리셋 파일이 너무 큽니다.");var presets=Backend.Json().Deserialize<List<Preset>>(File.ReadAllText(load.FileName));foreach(var preset in presets)backend.SavePreset(preset);result=presets.Count;}break;
    case "version":await backend.CheckVersion();result=backend.engineVersion;break;
    default:throw new Exception("알 수 없는 동작입니다.");
   }
   Send(new {type="reply",id=id,data=result});
  }catch(Exception ex){Send(new {type="reply",id=id,error=Backend.Redact(ex.Message)});}
 }
 static string Get(string json,string key){return Convert.ToString(Backend.Json().Deserialize<Dictionary<string,object>>(json)[key]);}
 void OnClosing(object sender,FormClosingEventArgs e) {if(backend.state.jobs.Any(j=>j.status=="running"||j.status=="queued")&&MessageBox.Show("진행 중인 작업을 중단하고 종료할까요? 다음 실행에서 재시도할 수 있습니다.","Flow",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){e.Cancel=true;return;}backend.Dispose();}
}
static class Program {
 [STAThread] static void Main() {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  using(var mutex=new Mutex(false,"FlowPortable_"+AppDomain.CurrentDomain.BaseDirectory.GetHashCode())){
   bool owned=false;try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
   if(!owned){MessageBox.Show("이 폴더의 Flow가 이미 실행 중입니다.");return;}
   try{Application.Run(new MainWindow());}finally{mutex.ReleaseMutex();}
  }
 }
}
}
