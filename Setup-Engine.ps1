param([string]$AppDirectory = $PSScriptRoot, [string]$DownloadCache)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskAppRoot = [IO.Path]::GetFullPath($AppDirectory)
$taskEngineRoot = Join-Path $taskAppRoot 'engine'
if (-not $DownloadCache) { $DownloadCache = Join-Path $taskAppRoot '.component-cache' }
New-Item -ItemType Directory -Force -Path $taskEngineRoot,$DownloadCache | Out-Null
$taskRelease = Invoke-RestMethod -Uri 'https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest'
$taskFfmpeg = $taskRelease.assets | Where-Object { $_.name -eq 'ffmpeg-master-latest-win64-gpl-shared.zip' } | Select-Object -First 1
if (-not $taskFfmpeg -or $taskFfmpeg.digest -notmatch '^sha256:[0-9a-f]{64}$') { throw 'The official FFmpeg release has no verified SHA256 metadata.' }
$taskComponents = @(
 @{Name='yt-dlp.exe';Url='https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp.exe';SHA256='33121d192d706e62f684a06f80918e3f3a8ba52ac3a65213b6951c39c7d9f555'},
 @{Name='deno.zip';Url='https://github.com/denoland/deno/releases/download/v2.9.8/deno-x86_64-pc-windows-msvc.zip';SHA256='cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf'},
 @{Name='ffmpeg.zip';Url=$taskFfmpeg.browser_download_url;SHA256=$taskFfmpeg.digest.Substring(7)}
)
foreach ($taskComponent in $taskComponents) {
 $taskArchivePath = Join-Path $DownloadCache $taskComponent.Name
 $taskMatches = (Test-Path -LiteralPath $taskArchivePath) -and ((Get-FileHash -LiteralPath $taskArchivePath -Algorithm SHA256).Hash -eq $taskComponent.SHA256)
 if (-not $taskMatches) {
  Write-Host ('Downloading official component: ' + $taskComponent.Name)
  $taskPartial = $taskArchivePath + '.part'
  Invoke-WebRequest -Uri $taskComponent.Url -OutFile $taskPartial -UseBasicParsing
  if ((Get-FileHash -LiteralPath $taskPartial -Algorithm SHA256).Hash -ne $taskComponent.SHA256) { throw ('SHA256 mismatch: ' + $taskComponent.Name) }
  Move-Item -LiteralPath $taskPartial -Destination $taskArchivePath -Force
 }
 Write-Host ('Verified: ' + $taskComponent.Name)
 if ($taskComponent.Name -eq 'yt-dlp.exe') { Copy-Item -LiteralPath $taskArchivePath -Destination (Join-Path $taskEngineRoot 'yt-dlp.exe') -Force; continue }
 $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskArchivePath)
 try {
  foreach ($taskEntry in $taskArchive.Entries) {
   $taskWanted = if ($taskComponent.Name -eq 'deno.zip') { $taskEntry.FullName -eq 'deno.exe' } else { $taskEntry.FullName -match '/bin/[^/]+\.(dll|exe)$' -and ($taskEntry.Name -like '*.dll' -or $taskEntry.Name -in @('ffmpeg.exe','ffprobe.exe')) }
   if (-not $taskWanted) { continue }
   $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskEngineRoot $taskEntry.Name))
   if (-not $taskTarget.StartsWith($taskEngineRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escaped the app folder.' }
   $taskRead = $taskEntry.Open()
   $taskWrite = [IO.File]::Create($taskTarget)
   try { $taskRead.CopyTo($taskWrite) } finally { $taskWrite.Dispose(); $taskRead.Dispose() }
  }
 } finally { $taskArchive.Dispose() }
}
$taskComponents | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskAppRoot 'component-manifest.json') -Encoding UTF8
Write-Host 'Media components are ready in the app folder. No global settings were changed.'
