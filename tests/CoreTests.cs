using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Flow {
public static class Tests {
 static List<object> passed=new List<object>();
 static void Assert(bool condition,string label){if(!condition)throw new Exception(label);passed.Add(new {test=label,status="PASS"});if(!label.StartsWith("catalog form mapping"))Console.WriteLine("PASS "+label);}
 static async Task<Job> Finish(Backend b,Job j,int seconds=45){DateTime start=DateTime.UtcNow;while(j.status=="queued"||j.status=="running"){if((DateTime.UtcNow-start).TotalSeconds>seconds)throw new Exception("Timeout: "+j.title);await Task.Delay(100);}return j;}
 static async Task Run(string root,string url,string scratch){
  string data=Path.Combine(scratch,"core-data"),downloads=Path.Combine(scratch,"다운로드 폴더 & unicode");
  Directory.CreateDirectory(scratch);
  using(var b=new Backend(root,x=>{},data)){
   await b.CheckVersion();Assert(b.engineVersion==b.catalog.version,"engine/catalog versions match");
   Assert(b.catalog.options.Count==323,"complete parser catalog has 323 definitions");
   Assert(Backend.Quote("a b")=="\"a b\""&&Backend.Quote("C:\\tail\\")=="\"C:\\tail\\\\\""&&Backend.Quote("a\"b")=="\"a\\\"b\"","Windows argument quoting spaces, quotes and trailing slashes");
   foreach(var o in b.catalog.options){var r=new JobRequest{mode="command",allowRisk=true,settings=new List<Setting>{new Setting{flag=o.flag,values=Enumerable.Repeat("test",o.nargs).ToList()}}};var args=b.BuildArgs(r);Assert(args.Contains(o.flag),"catalog form mapping "+o.flag);}
   var duplicate=new JobRequest{mode="command",settings=new List<Setting>{new Setting{flag="--sub-langs",values=new List<string>{"ko"}},new Setting{flag="--sub-langs",values=new List<string>{"en"}}}};
   var da=b.BuildArgs(duplicate);Assert(da.IndexOf("ko")<da.IndexOf("en"),"repeated option ordering preserved");
   bool rejected=false;try{b.Submit(new JobRequest{mode="command",settings=new List<Setting>{new Setting{flag="--exec",values=new List<string>{"not-run"}}}});}catch{rejected=true;}Assert(rejected,"external code requires explicit approval");
   var preview=(await b.Analyze(new JobRequest{urls=new List<string>{url}}));Assert(preview!=null,"real metadata extraction from HTTP fixture");
   var job=await Finish(b,b.Submit(new JobRequest{urls=new List<string>{url},folder=downloads}));
   Assert(job.status=="completed"&&job.files.Count==1&&File.Exists(job.files[0]),"real download to Korean spaced folder and output confirmation");
   var clip=await Finish(b,b.Submit(new JobRequest{urls=new List<string>{url},folder=downloads,settings=new List<Setting>{new Setting{flag="--download-sections",values=new List<string>{"*0.500-1.500"}},new Setting{flag="--force-keyframes-at-cuts"}}}));
   Assert(clip.status=="completed"&&clip.files.Count==1&&clip.files[0]!=job.files[0],"real precise range download has a distinct output from full video");
   var clip2=await Finish(b,b.Submit(new JobRequest{urls=new List<string>{url},folder=downloads,settings=new List<Setting>{new Setting{flag="--download-sections",values=new List<string>{"*1.500-2.500"}},new Setting{flag="--force-keyframes-at-cuts"}}}));
   Assert(clip2.status=="completed"&&clip2.files.Count==1&&clip2.files[0]!=clip.files[0],"different ranges never silently reuse the same default filename");
   var mp3=await Finish(b,b.Submit(new JobRequest{urls=new List<string>{url},folder=Path.Combine(downloads,"audio"),profile="mp3"}));
   Assert(mp3.status=="completed"&&mp3.files.Any(f=>f.EndsWith(".mp3")),"FFmpeg MP3 extraction and postprocessing");
   var command=await Finish(b,b.Submit(new JobRequest{mode="command",settings=new List<Setting>{new Setting{flag="--help"}}}));
   Assert(command.status=="completed"&&command.output.Contains("--format"),"GUI tool execution returns real engine output");
   var stdin=await Finish(b,b.Submit(new JobRequest{mode="command",folder=downloads,stdinText=url+"\n",settings=new List<Setting>{new Setting{flag="--batch-file",values=new List<string>{"-"}},new Setting{flag="--get-title"}}}));
   Assert(stdin.status=="completed"&&stdin.output.Length>0,"standard input to batch-file dash");
   var stream=await Finish(b,b.Submit(new JobRequest{mode="command",folder=Path.Combine(downloads,"stream"),urls=new List<string>{url},settings=new List<Setting>{new Setting{flag="--output",values=new List<string>{"-"}}}}));
   Assert(stream.status=="completed"&&stream.files.Count==1&&new FileInfo(stream.files[0]).Length>1000,"binary stdout saved without UTF8 corruption");
   var slow=b.Submit(new JobRequest{urls=new List<string>{url},folder=Path.Combine(downloads,"slow"),settings=new List<Setting>{new Setting{flag="--limit-rate",values=new List<string>{"1K"}}}});
   await Task.Delay(1800);b.Stop(slow.id);await Finish(b,slow);Assert(slow.status=="cancelled","cancel terminates engine process tree");
   // Reuse the exact cancelled job, changing only the rate argument for a bounded fixture test.
   int rate=slow.args.IndexOf("--limit-rate");slow.args[rate+1]="10M";b.Retry(slow.id);await Finish(b,slow);Assert(slow.status=="completed","retry a cancelled task with continuation");
   b.SavePreset(new Preset{name="test preset",profile="best",settings=new List<Setting>{new Setting{flag="--add-headers",values=new List<string>{"X-Test:flow-private-marker"}},new Setting{flag="--sub-langs",values=new List<string>{"ko"}}}});
   Assert(b.state.presets.Last().settings.Count==1,"sensitive settings excluded from presets");
   var privateJob=await Finish(b,b.Submit(new JobRequest{mode="command",urls=new List<string>{url},settings=new List<Setting>{new Setting{flag="--add-headers",values=new List<string>{"X-Test:flow-private-marker"}},new Setting{flag="--get-title"}}}));
   string saved=File.ReadAllText(Path.Combine(data,"state.json"));Assert(!saved.Contains("flow-private-marker")&&!saved.Contains("stdinText\":\"http"),"sensitive values and stdin absent from persisted state");
   b.Preferences(downloads,"dark",2);Assert(b.state.theme=="dark","preferences persist");
  }
  using(var b=new Backend(root,x=>{},data)){Assert(b.state.jobs.Count>=7&&b.state.presets.Count==1,"jobs and presets restored after restart");Assert(b.state.jobs.Where(j=>j.sensitive).All(j=>j.requiresInput&&j.args.Count==0),"restored private jobs require reentry");}
  File.WriteAllText(Path.Combine(scratch,"core-results.json"),Backend.Json().Serialize(new{status="PASS",checks=passed.Count,tests=passed}));
 }
 public static int Main(string[] args){try{Run(args[0],args[1],args[2]).GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
}
