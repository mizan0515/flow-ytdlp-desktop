param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskProject = $PSScriptRoot
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskProject 'dist/Flow-1.1-win-x64' }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $taskOutput 'ui') | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll'))) { throw 'Run python tools/prepare.py --sdk-only first, or build into an extracted release folder.' }
Copy-Item -Path (Join-Path $taskProject 'ui/*') -Destination (Join-Path $taskOutput 'ui') -Force
$taskReferences = @('/r:System.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/r:' + (Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll')),('/r:' + (Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll')))
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + (Join-Path $taskOutput 'Flow.exe')) ('/win32manifest:' + (Join-Path $taskProject 'src/app.manifest')) @taskReferences (Join-Path $taskProject 'src/App.cs')
if ($LASTEXITCODE -ne 0) { throw 'Flow compilation failed' }
Write-Output ('Built: ' + (Join-Path $taskOutput 'Flow.exe'))
