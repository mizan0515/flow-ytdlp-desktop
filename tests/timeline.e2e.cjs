// Exercises the actual Windows WebView2 host via its opt-in local test port.
const {chromium}=require(process.argv[2]);
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),http=require('node:http'),{spawn,spawnSync}=require('node:child_process');
const output=path.resolve(process.argv[3]),scratch=path.resolve(process.argv[4]);fs.mkdirSync(scratch,{recursive:true});
const exe=path.join(output,'Flow.exe'),fixture=path.join(scratch,'timeline-fixture.mp4');
const ffmpeg=path.join(output,'engine/ffmpeg.exe'),ffprobe=path.join(output,'engine/ffprobe.exe');
assert.equal(spawnSync(ffmpeg,['-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc2=size=480x270:rate=24','-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','12','-c:v','libx264','-g','48','-pix_fmt','yuv420p','-c:a','aac','-movflags','+faststart',fixture],{windowsHide:true}).status,0);
const tests=[];function check(name,value){assert(value,name);tests.push({name,status:'PASS'});console.log('PASS',name);}
let browser,application;
const server=http.createServer((req,res)=>{if(req.url!=='/timeline-fixture.mp4'){res.writeHead(404).end();return;}const size=fs.statSync(fixture).size;let start=0,end=size-1;const match=/bytes=(\d+)-(\d*)/.exec(req.headers.range||'');if(match){start=Number(match[1]);if(match[2])end=Math.min(end,Number(match[2]));}if(start>=size){res.writeHead(416).end();return;}const headers={'Content-Type':'video/mp4','Content-Length':end-start+1,'Accept-Ranges':'bytes'};if(match)headers['Content-Range']=`bytes ${start}-${end}/${size}`;res.writeHead(match?206:200,headers);fs.createReadStream(fixture,{start,end}).pipe(res);});
(async()=>{
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const url=`http://127.0.0.1:${server.address().port}/timeline-fixture.mp4`;
 application=spawn(exe,['--inspect-port','50102','--test-data',path.join(scratch,'app-data')],{stdio:'ignore'});
 for(let attempt=0;attempt<40;attempt++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:50102');break;}catch{await new Promise(r=>setTimeout(r,250));}}
 assert(browser,'test host started');
 let page;
 for(let attempt=0;attempt<40;attempt++){page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://flow.local/'));if(page)break;await new Promise(r=>setTimeout(r,250));}
 assert(page,'expected local app target');
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.waitForFunction(()=>initialized&&!!app.engineVersion);
 check('basic screen keeps timeline collapsed',!await page.locator('#range-panel').isVisible());
 await page.locator('#urls').fill(url);await page.locator('#range-enabled').check();
 await page.waitForFunction(()=>rangeEditor.duration>0,{},{timeout:45000});
 const duration=await page.evaluate(()=>rangeEditor.duration);check('real metadata/FFprobe duration opens timeline',Math.abs(duration-12)<.1);
 await page.locator('#timeline-viewport').scrollIntoViewIfNeeded();
 const track=await page.locator('#timeline-track').boundingBox();
 async function drag(locator,delta){const b=await locator.boundingBox();await page.mouse.move(b.x+b.width/2,b.y+b.height/2);await page.mouse.down();await page.mouse.move(b.x+b.width/2+delta,b.y+b.height/2,{steps:12});await page.mouse.up();}
 async function range(){return page.evaluate(()=>rangeEditor.read());}
 await drag(page.locator('#range-in-handle'),track.width*.16);let r=await range();check('dragging left handle trims start',r.start>1.5&&r.start<2.3);
 await drag(page.locator('#range-out-handle'),-track.width*.3);r=await range();check('dragging right handle trims end',r.end>8&&r.end<9);
 const shorter={...r};await drag(page.locator('#range-in-handle'),-track.width*.03);await drag(page.locator('#range-out-handle'),track.width*.03);r=await range();check('dragging handles outward extends the selection',r.start<shorter.start-.2&&r.end>shorter.end+.2);
 const before={...r};await drag(page.locator('#range-drag-body'),track.width*.08);r=await range();check('dragging whole selection preserves duration',r.start>before.start+.5&&Math.abs((r.end-r.start)-(before.end-before.start))<.02);
 const stable={...r};await page.locator('#timeline-zoom-in').click();check('zoom increases scale without changing selected times',await page.locator('#timeline-zoom-label').innerText()==='200%'&&Math.abs((await range()).start-stable.start)<.001);
 await page.locator('#timeline-fit').click();check('fit restores entire source timeline',await page.locator('#timeline-zoom-label').innerText()==='100%');
 await page.locator('#range-in-handle').focus();let keyboardBefore=await range();await page.keyboard.press('ArrowRight');r=await range();check('handle keyboard adjusts by 0.1 seconds',Math.abs(r.start-keyboardBefore.start-.1)<.002);
 await page.keyboard.press('Shift+ArrowRight');r=await range();check('Shift keyboard adjusts by one second',Math.abs(r.start-keyboardBefore.start-1.1)<.002);
 const ruler=await page.locator('#timeline-ruler').boundingBox();await page.mouse.click(ruler.x+ruler.width*.4,ruler.y+10);const cursor=await page.evaluate(()=>rangeEditor.cursor);await page.locator('#range-in-at-cursor').click();r=await range();check('timeline cursor can set an in point',Math.abs(r.start-cursor)<.002);
 await page.locator('#range-start').fill('6');await page.locator('#range-end').fill('4');await page.locator('#download').click();check('reverse selection is blocked before engine submission',(await page.locator('#form-error').innerText()).includes('종료 시간')&&await page.evaluate(()=>app.state.jobs.length)===0);
 await page.locator('#range-start').fill('1.250');await page.locator('#range-end').fill('3.250');await page.locator('#range-end').blur();check('time inputs synchronize timeline and length',(await page.locator('#range-duration').innerText())==='2.000초');
 await page.locator('#folder').fill(path.join(scratch,'실제 구간 결과'));
 await page.locator('#quick-preset').click();await page.locator('#confirm-input').fill('구간 테스트');await page.locator('#confirm-yes').click();await page.waitForFunction(()=>app.state.presets.length===1);
 await page.locator('#range-enabled').uncheck();await page.locator('#saved-preset').selectOption({label:'구간 테스트'});check('preset restores graphical selection without duplicate raw options',await page.locator('#range-enabled').isChecked()&&(await range()).start===1.25&&await page.evaluate(()=>selected.filter(s=>s.flag==='--download-sections').length)===0);
 await page.locator('#urls').fill(url+'?changed');check('changing source invalidates old duration but preserves explicit preset times',await page.evaluate(()=>rangeEditor.duration)===null&&(await page.locator('#range-start').inputValue())==='00:00:01.250');await page.locator('#urls').fill(url);await page.locator('#analyze').click();await page.waitForFunction(()=>rangeEditor.duration>0);
 // This uses the normal production download button and actual engine process.
 await page.locator('#download').click();await page.waitForFunction(()=>app.state.jobs.length&& !['running','queued'].includes(app.state.jobs[0].status),{},{timeout:60000});
 const job=await page.evaluate(()=>app.state.jobs[0]);check('UI range download completes with a real output file',job.status==='completed'&&job.files.length===1&&fs.existsSync(job.files[0]));
 const measured=Number(spawnSync(ffprobe,['-v','error','-show_entries','format=duration','-of','default=noprint_wrappers=1:nokey=1',job.files[0]],{encoding:'utf8',windowsHide:true}).stdout.trim());check('selected two seconds produce a two-second media file',Math.abs(measured-2)<.15);
 await page.locator('[data-nav=new]').click();await page.locator('.range-section').screenshot({path:path.join(scratch,'timeline.png')});
 // Full download remains unchanged when the optional range switch is off.
 await page.locator('#range-enabled').uncheck();const fullArgs=await page.evaluate(()=>request().settings);check('switching off removes range and precision options from download',!fullArgs.some(s=>s.flag==='--download-sections'||s.flag==='--force-keyframes-at-cuts'));
 check('no uncaught UI errors',errors.length===0);
 fs.writeFileSync(path.join(scratch,'timeline-results.json'),JSON.stringify({status:'PASS',duration,outputDuration:measured,tests},null,2));
 console.log('Evidence:',scratch);
 await browser.close();server.close();
 // Only the isolated test instance launched above is closed.
 spawnSync('taskkill.exe',['/PID',String(application.pid),'/T','/F'],{windowsHide:true});
})().catch(async e=>{console.error(e);if(browser){const p=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://flow.local/'));if(p)await p.screenshot({path:path.join(scratch,'failure.png'),fullPage:true}).catch(()=>{});await browser.close().catch(()=>{});}server.close();if(application)spawnSync('taskkill.exe',['/PID',String(application.pid),'/T','/F'],{windowsHide:true});process.exitCode=1;});
