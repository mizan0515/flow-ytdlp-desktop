(function(root){
 'use strict';
 const escape=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const links=s=>String(s).split(/\r?\n/).map(x=>x.trim()).filter(x=>x&&!/^[#;\]]/.test(x));
 const matches=(o,q)=>[o.flag,...o.flags,o.label,o.help,o.group].join(' ').toLowerCase().includes(q.trim().toLowerCase());
 const validSettings=(settings,catalog)=>settings.every(s=>{const o=catalog.options.find(o=>o.flags.includes(s.flag));return o&&s.values.length===(o.takesValue?o.nargs:0)&&s.values.every(v=>typeof v==='string'&&!v.includes('\0'));});
 const cleanPreset=(settings,catalog)=>settings.filter(s=>{const o=catalog.options.find(o=>o.flags.includes(s.flag));return o&&!o.sensitive;}).map(s=>({flag:s.flag,values:[...s.values]}));
 const roundTime=n=>Math.round(n*1000)/1000;
 function parseTime(value){
  const text=String(value).trim();
  if(!text)return null;
  if(!/^\d+(?::\d{1,2}){0,2}(?:\.\d{1,3})?$/.test(text))throw new Error('시간은 초, 분:초 또는 시:분:초 형식으로 입력하세요. 예: 01:23.500');
  const parts=text.split(':').map(Number);
  if(parts.slice(1).some(n=>n>=60))throw new Error('분과 초는 0부터 59 사이로 입력하세요.');
  const seconds=parts.reduce((n,part)=>n*60+part,0);
  if(!Number.isFinite(seconds)||seconds>359999999)throw new Error('입력한 시간이 너무 큽니다.');
  return roundTime(seconds);
 }
 function formatTime(seconds,decimals=true){
  if(!Number.isFinite(seconds))return '영상 끝';
  const ms=Math.round(Math.max(0,seconds)*1000),hours=Math.floor(ms/3600000),minutes=Math.floor(ms/60000)%60,secs=Math.floor(ms/1000)%60;
  return `${String(hours).padStart(2,'0')}:${String(minutes).padStart(2,'0')}:${String(secs).padStart(2,'0')}${decimals?'.'+String(ms%1000).padStart(3,'0'):''}`;
 }
 function checkedRange(start,end,duration=null){
  if(start==null)start=0;
  if(end==null)end=duration??Infinity;
  if(!Number.isFinite(start)||start<0)throw new Error('시작 시간은 0초 이상이어야 합니다.');
  if(!(end>start))throw new Error('종료 시간은 시작 시간보다 뒤여야 합니다.');
  if(Number.isFinite(duration)&&(start>=duration||end>duration+.001))throw new Error('선택 구간이 영상 길이를 넘습니다.');
  return {start:roundTime(start),end:Number.isFinite(end)?roundTime(end):Infinity};
 }
 function dragRange(range,mode,delta,duration){
  const minimum=Math.min(.01,duration),clamp=(v,a,b)=>Math.min(b,Math.max(a,v));
  let {start,end}=range;
  if(mode==='start')start=clamp(start+delta,0,end-minimum);
  else if(mode==='end')end=clamp(end+delta,start+minimum,duration);
  else if(mode==='move'){const length=end-start;start=clamp(start+delta,0,duration-length);end=start+length;}
  return {start:roundTime(start),end:roundTime(end)};
 }
 function rangeSettings(range,precise){
  const r=checkedRange(range.start,range.end);
  const settings=[{flag:'--download-sections',values:[`*${r.start.toFixed(3)}-${Number.isFinite(r.end)?r.end.toFixed(3):'inf'}`]}];
  if(precise)settings.push({flag:'--force-keyframes-at-cuts',values:[]});
  return settings;
 }
 const api={escape,links,matches,validSettings,cleanPreset,parseTime,formatTime,checkedRange,dragRange,rangeSettings,roundTime};
 if(typeof module!=='undefined')module.exports=api;else root.FlowCore=api;
})(typeof globalThis!=='undefined'?globalThis:this);
