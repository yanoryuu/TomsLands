# 人格マラソンを裏で始める（見張り役 marathon.py を隠しウィンドウの pythonw で起動）。
#   powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/start.ps1
# 止める: tools/persona_play/marathon/stop.ps1
param(
    [string]$Config = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = (Resolve-Path (Join-Path $here '..\..\..')).Path
$pidFile = Join-Path $project 'Library\PersonaMarathon\runner.pid'

if (Test-Path $pidFile) {
    $old = Get-Content $pidFile -ErrorAction SilentlyContinue
    if ($old -and (Get-Process -Id ([int]$old) -ErrorAction SilentlyContinue)) {
        Write-Host "もう動いています（pid $old）。止めるときは stop.ps1"
        exit 1
    }
}

$pyw = Join-Path $env:WINDIR 'pyw.exe'
if (-not (Test-Path $pyw)) { $pyw = 'pythonw' }
$script = Join-Path $here 'marathon.py'
$argList = @('-3', "`"$script`"", '--project', "`"$project`"")
if ($Config) { $argList += @('--config', "`"$Config`"") }
# resume.ps1 が同じ設定で続きから始められるように、使った設定ファイルを覚えておく
$ipc = Join-Path $project 'Library\PersonaMarathon'
New-Item -ItemType Directory -Force $ipc | Out-Null
Set-Content -Path (Join-Path $ipc 'last_config.txt') -Value $Config -Encoding utf8
$p = Start-Process -FilePath $pyw -ArgumentList $argList -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 3
Write-Host "人格マラソンを始めました（pid $($p.Id)）。"
Write-Host "進み具合: $project\Recordings\marathon_<日時>\progress.log / 朝のまとめ: 同じフォルダの summary.md"
