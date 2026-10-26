'use strict';
class FlowRangeEditor {
 constructor({onChange,onAnalyze,getUrls}){
  this.onChange=onChange;this.onAnalyze=onAnalyze;this.duration=null;this.zoom=1;this.cursor=0;this.drag=null;this.needsDefault=true;this.userEdited=false;this.getUrls=getUrls;
  this.$=id=>document.getElementById(id);
  this.$('range-enabled').addEventListener('change',()=>{this.render();this.onChange();if(this.enabled&&!this.duration&&FlowCore.links(this.getUrls()).length===1)this.onAnalyze();});
  for(const id of ['range-start','range-end']){
   this.$(id).addEventListener('input',()=>{this.needsDefault=false;this.userEdited=true;this.render();this.onChange();});
   this.$(id).addEventListener('change',()=>{try{const t=FlowCore.parseTime(this.$(id).value);if(t!=null)this.$(id).value=FlowCore.formatTime(t);}catch{}this.render();});
  }
  this.$('range-precise').addEventListener('change',()=>this.onChange());
  this.$('range-analyze').onclick=()=>this.onAnalyze();
  this.$('timeline-zoom-in').onclick=()=>this.setZoom(this.zoom*2);
  this.$('timeline-zoom-out').onclick=()=>this.setZoom(this.zoom/2);
  this.$('timeline-fit').onclick=()=>this.setZoom(1);
  this.$('range-in-at-cursor').onclick=()=>this.cursorBoundary('start');
  this.$('range-out-at-cursor').onclick=()=>this.cursorBoundary('end');
  const track=this.$('timeline-track');
  track.addEventListener('pointerdown',e=>this.pointerDown(e));
  track.addEventListener('pointermove',e=>this.pointerMove(e));
  track.addEventListener('pointerup',e=>this.pointerEnd(e));
  track.addEventListener('pointercancel',e=>this.pointerEnd(e,true));
  track.addEventListener('lostpointercapture',()=>{this.drag=null;track.classList.remove('dragging');});
  for(const id of ['range-in-handle','range-out-handle','range-drag-body'])this.$(id).addEventListener('keydown',e=>this.keyDown(e));
  this.resize=new ResizeObserver(()=>this.renderRuler());this.resize.observe(this.$('timeline-viewport'));
  this.render();
 }
 get enabled(){return this.$('range-enabled').checked;}
 read(){return FlowCore.checkedRange(FlowCore.parseTime(this.$('range-start').value),FlowCore.parseTime(this.$('range-end').value),this.duration);}
 settings(){if(!this.enabled)return [];return FlowCore.rangeSettings(this.read(),this.$('range-precise').checked);}
 summary(){if(!this.enabled)return '';try{const r=this.read();return `${FlowCore.formatTime(r.start,false)} → ${FlowCore.formatTime(r.end,false)}`;}catch{return '구간 시간 확인 필요';}}
 invalidate(){this.duration=null;this.zoom=1;this.cursor=0;if(!this.userEdited){this.needsDefault=true;this.$('range-start').value='00:00:00.000';this.$('range-end').value='';}this.render();this.onChange();}
 setMedia(info){
  const n=Number(info.duration);
  this.duration=Number.isFinite(n)&&n>0&&info._type!=='playlist'&&!info.entries&&!info.is_live?n:null;
  this.$('timeline-empty').querySelector('p').textContent=this.duration?'링크를 확인하면 타임라인이 열립니다.':info.entries?'타임라인은 단일 영상에서 사용할 수 있어요. 시간 입력은 목록의 각 영상에 적용됩니다.':'영상 길이를 확인하지 못했습니다. 시간을 직접 입력하거나 영상 정보를 다시 확인하세요.';
  this.zoom=1;this.cursor=0;
  if(this.duration&&this.needsDefault){this.$('range-start').value='00:00:00.000';this.$('range-end').value=FlowCore.formatTime(this.duration);this.needsDefault=false;}
  this.render();this.onChange();
 }
 write(range){this.$('range-start').value=FlowCore.formatTime(range.start);this.$('range-end').value=FlowCore.formatTime(range.end);this.needsDefault=false;this.userEdited=true;this.render();this.onChange();}
 restore(settings){
  this.$('range-enabled').checked=false;this.$('range-start').value='00:00:00.000';this.$('range-end').value='';this.$('range-precise').checked=true;this.needsDefault=true;this.userEdited=false;
  const parts=settings.filter(s=>s.flag==='--download-sections');
  if(parts.length===1){
   const match=/^\*(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?|inf)$/.exec(parts[0].values[0]);
   if(match){this.$('range-enabled').checked=true;this.$('range-start').value=FlowCore.formatTime(Number(match[1]));this.$('range-end').value=match[2]==='inf'?'':FlowCore.formatTime(Number(match[2]));this.$('range-precise').checked=settings.filter(s=>['--force-keyframes-at-cuts','--no-force-keyframes-at-cuts'].includes(s.flag)).at(-1)?.flag==='--force-keyframes-at-cuts';this.needsDefault=false;this.userEdited=true;settings=settings.filter(s=>s!==parts[0]&&!['--force-keyframes-at-cuts','--no-force-keyframes-at-cuts'].includes(s.flag));}
  }
  this.render();return settings;
 }
 setZoom(value){if(!this.duration)return;this.zoom=Math.max(1,Math.min(16,value));const r=this.tryRead();this.render();const view=this.$('timeline-viewport');if(r)view.scrollLeft=((r.start+r.end)/2/this.duration)*this.$('timeline-track').clientWidth-view.clientWidth/2;}
 tryRead(){try{return this.read();}catch{return null;}}
 render(){
  const enabled=this.enabled,ready=!!this.duration;
  this.$('range-panel').classList.toggle('hidden',!enabled);this.$('range-enabled').setAttribute('aria-expanded',enabled);
  this.$('timeline-empty').classList.toggle('hidden',ready);this.$('timeline-viewport').classList.toggle('hidden',!ready);this.$('timeline-readout').classList.toggle('hidden',!ready);
  this.$('timeline-source').textContent=ready?'전체 '+FlowCore.formatTime(this.duration,false):'영상 길이 확인 필요';
  this.$('timeline-zoom-label').textContent=Math.round(this.zoom*100)+'%';
  this.$('timeline-zoom-in').disabled=!ready||this.zoom>=16;this.$('timeline-zoom-out').disabled=!ready||this.zoom<=1;this.$('timeline-fit').disabled=!ready;
  let range=null,error='';try{range=this.read();}catch(e){error=e.message;}
  this.$('range-error').textContent=error;this.$('range-error').classList.toggle('hidden',!enabled||!error);
  for(const id of ['range-start','range-end'])this.$(id).setAttribute('aria-invalid',enabled&&!!error?'true':'false');
  this.$('range-duration').textContent=range&&Number.isFinite(range.end)?(range.end-range.start).toFixed(3)+'초':range?'영상 끝까지':'시간 확인';
  this.$('timeline-track').style.width=(this.zoom*100)+'%';
  const selection=this.$('range-selection');selection.classList.toggle('hidden',!range||!ready);
  if(range&&ready){selection.style.left=(range.start/this.duration*100)+'%';selection.style.width=((range.end-range.start)/this.duration*100)+'%';this.$('range-clip-label').textContent=(range.end-range.start).toFixed(2)+'초';selection.classList.toggle('compact',(range.end-range.start)/this.duration*this.$('timeline-track').clientWidth<90);
   for(const [id,value,min,max] of [['range-in-handle',range.start,0,range.end-.001],['range-out-handle',range.end,range.start+.001,this.duration],['range-drag-body',range.start,0,this.duration-(range.end-range.start)]]){const el=this.$(id);el.setAttribute('aria-valuemin',Math.max(0,min));el.setAttribute('aria-valuemax',Math.max(0,max));el.setAttribute('aria-valuenow',value);el.setAttribute('aria-valuetext',FlowCore.formatTime(value));}
  }
  this.renderCursor();this.renderRuler();
 }
 renderCursor(){this.$('timeline-playhead').style.left=(this.duration?this.cursor/this.duration*100:0)+'%';this.$('timeline-cursor').textContent=FlowCore.formatTime(this.cursor);const r=this.tryRead();this.$('range-in-at-cursor').disabled=!r||this.cursor>=r.end;this.$('range-out-at-cursor').disabled=!r||this.cursor<=r.start;}
 renderRuler(){
  const ruler=this.$('timeline-ruler');if(!this.duration||!this.enabled)return;
  const width=this.$('timeline-track').clientWidth;if(width<=0)return;
  const target=this.duration/(width/85),steps=[.01,.02,.05,.1,.2,.5,1,2,5,10,15,30,60,120,300,600,900,1800,3600,7200,14400,43200,86400];
  const step=steps.find(n=>n>=target)||Math.ceil(target/86400)*86400;
  const fragment=document.createDocumentFragment();
  for(let value=0;value<this.duration;value+=step){const tick=document.createElement('span');tick.className='timeline-tick';tick.style.left=(value/this.duration*100)+'%';tick.textContent=FlowCore.formatTime(value,step<1).replace(/^00:/,'');fragment.append(tick);}
  ruler.replaceChildren(fragment);
 }
 timeAt(e){const rect=this.$('timeline-track').getBoundingClientRect();return Math.max(0,Math.min(this.duration,(e.clientX-rect.left)/rect.width*this.duration));}
 pointerDown(e){
  if(!this.duration||e.button!==0)return;
  const range=this.tryRead(),handle=e.target.closest('[data-drag]');
  if(handle&&!range)return;
  e.preventDefault();this.$('timeline-track').setPointerCapture(e.pointerId);
  this.drag={id:e.pointerId,mode:handle?.dataset.drag||'cursor',origin:this.timeAt(e),range};
  if(handle)handle.focus({preventScroll:true});this.$('timeline-track').classList.add('dragging');
  if(!handle){this.cursor=FlowCore.roundTime(this.timeAt(e));this.renderCursor();}
 }
 pointerMove(e){
  if(!this.drag||this.drag.id!==e.pointerId)return;
  if(this.drag.mode==='cursor'){this.cursor=FlowCore.roundTime(this.timeAt(e));this.renderCursor();return;}
  let delta=this.timeAt(e)-this.drag.origin;
  if(e.shiftKey)delta=Math.round(delta*10)/10;
  this.write(FlowCore.dragRange(this.drag.range,this.drag.mode,delta,this.duration));
 }
 pointerEnd(e,cancel=false){if(this.drag?.id!==e.pointerId)return;if(cancel&&this.drag.range)this.write(this.drag.range);this.drag=null;this.$('timeline-track').classList.remove('dragging');if(this.$('timeline-track').hasPointerCapture(e.pointerId))this.$('timeline-track').releasePointerCapture(e.pointerId);}
 keyDown(e){
  if(!this.duration)return;const r=this.tryRead();if(!r)return;
  let delta;if(['ArrowLeft','ArrowDown'].includes(e.key))delta=-(e.shiftKey?1:.1);else if(['ArrowRight','ArrowUp'].includes(e.key))delta=e.shiftKey?1:.1;else if(e.key==='Home')delta=-this.duration;else if(e.key==='End')delta=this.duration;else return;
  e.preventDefault();this.write(FlowCore.dragRange(r,e.currentTarget.dataset.drag,delta,this.duration));
 }
 cursorBoundary(mode){const r=this.tryRead();if(!r)return;const next={...r,[mode]:this.cursor};try{this.write(FlowCore.checkedRange(next.start,next.end,this.duration));}catch(e){this.$('range-error').textContent=e.message;this.$('range-error').classList.remove('hidden');}}
}
