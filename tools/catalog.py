"""Derive the complete GUI catalog from the pinned engine's option parser."""
import json, pathlib, sys, optparse
ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'work/deps/ytdlp-source/yt-dlp'))
from yt_dlp.options import create_parser
from yt_dlp.version import __version__
GROUPS={'General Options':'일반·확장','Network Options':'네트워크','Geo-restriction':'지역 설정','Video Selection':'항목·필터','Download Options':'다운로드','Filesystem Options':'파일·인증','Thumbnail Options':'썸네일','Internet Shortcut Options':'인터넷 바로가기','Verbosity and Simulation Options':'조회·진단','Workarounds':'호환성','Video Format Options':'영상 형식','Subtitle Options':'자막','Authentication Options':'인증','Post-Processing Options':'후처리','SponsorBlock Options':'SponsorBlock','Extractor Options':'추출기'}
LABELS={'format':'영상·음성 형식 선택식','format-sort':'형식 정렬 기준','output':'파일명 템플릿','paths':'저장 경로','extract-audio':'오디오 추출','audio-format':'오디오 형식','audio-quality':'오디오 품질','write-subs':'자막 저장','write-auto-subs':'자동 자막 저장','sub-langs':'자막 언어','embed-subs':'자막 내장','embed-thumbnail':'표지 내장','embed-metadata':'메타데이터 내장','cookies':'쿠키 파일','cookies-from-browser':'브라우저 쿠키 사용','username':'사용자 이름','password':'비밀번호','proxy':'프록시','download-sections':'시간·챕터 구간','playlist-items':'재생목록 항목','playlist-reverse':'재생목록 역순','download-archive':'중복 다운로드 기록','concurrent-fragments':'동시 조각 수','limit-rate':'다운로드 속도 제한','retries':'재시도 횟수','remux-video':'컨테이너 변경','recode-video':'영상 재인코딩','split-chapters':'챕터별 파일 분리','sponsorblock-remove':'SponsorBlock 구간 제거','extractor-args':'사이트별 추출 인수','exec':'외부 명령 실행','plugin-dirs':'플러그인 폴더','config-locations':'설정 파일 불러오기','print':'정보 출력 템플릿','dump-single-json':'메타데이터 JSON 조회','simulate':'다운로드 없이 확인','skip-download':'미디어 다운로드 생략','update-to':'엔진 버전 변경','list-formats':'형식 목록 조회','list-subs':'자막 목록 조회','live-from-start':'라이브 처음부터','wait-for-video':'방송 시작 대기','ffmpeg-location':'FFmpeg 위치','js-runtimes':'JavaScript 실행 환경','match-filters':'다운로드 조건식','dateafter':'이 날짜 이후','datebefore':'이 날짜 이전'}
SENSITIVE={'--cookies','--cookies-from-browser','--username','--password','--video-password','--ap-username','--ap-password','--twofactor','--netrc','--netrc-location','--netrc-cmd','--add-headers','--proxy','--geo-verification-proxy','--client-certificate','--client-certificate-key','--client-certificate-password','--extractor-args','--alias','--config-locations','--exec','--exec-before-download','--postprocessor-args','--downloader-args'}
RISK={'--exec','--exec-before-download','--alias','--netrc-cmd','--plugin-dirs','--config-locations','--remote-components','--enable-file-urls','--no-check-certificates','--prefer-insecure','--update','--update-to','--rm-cache-dir','--mark-watched','--print-traffic','--dump-pages','--write-pages','--no-clean-info-json','--force-overwrites','--post-overwrites','--postprocessor-args','--downloader-args','--use-postprocessor'}
parser=create_parser()
records=[]
for group in [parser]+parser.option_groups:
 for o in group.option_list:
  flags=o._long_opts+o._short_opts
  if not flags: continue
  flag=next((f for f in flags if f.startswith('--')),flags[0])
  help_text=o.help if o.help!=optparse.SUPPRESS_HELP else 'Deprecated or compatibility option. See engine documentation.'
  try: help_text=parser.formatter.expand_default(o) if o.help!=optparse.SUPPRESS_HELP else help_text
  except Exception: pass
  takes=o.takes_value()
  records.append(dict(flag=flag,flags=flags,label=LABELS.get(flag.lstrip('-'),flag.lstrip('-').replace('-',' ')),group=GROUPS.get(getattr(group,'title','General Options'),getattr(group,'title','일반·확장')),help=help_text or '',takesValue=takes,nargs=o.nargs if takes else 0,metavar=o.metavar or 'VALUE',choices=o.choices or [],repeatable=o.action in ['append','callback'],action=o.action,sensitive=bool(set(flags)&SENSITIVE),risk=bool(set(flags)&RISK),hidden=o.help==optparse.SUPPRESS_HELP))
priority=list(LABELS)
records.sort(key=lambda r:(r['hidden'],priority.index(r['flag'].lstrip('-')) if r['flag'].lstrip('-') in priority else 999))
catalog=dict(version=__version__,source='https://github.com/yt-dlp/yt-dlp/tree/'+__version__,options=records,optionCount=len(records),flagCount=sum(len(r['flags']) for r in records))
(ROOT/'ui/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in catalog.items() if k!='options'}))
