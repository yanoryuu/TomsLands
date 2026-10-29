# 人格マラソンを一時停止する。ふつうは走っているランが終わってから止める（エディタは開いたまま待機）。続きは resume.ps1。
#   powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/pause.ps1        # 今のランが終わったら止める
#   powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/pause.ps1 -Now   # 今すぐ止める（走っているランも止める）
param([switch]$Now)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = (Resolve-Path (Join-Path $here '..\..\..')).Path
$ipc = Join-Path $project 'Library\PersonaMarathon'
New-Item -ItemType Directory -Force $ipc | Out-Null
$pidFile = Join-Path $ipc 'runner.pid'
$runnerPid = if (Test-Path $pidFile) { [int](Get-Content $pidFile) } else { 0 }
$proc = if ($runnerPid) { Get-Process -Id $runnerPid -ErrorAction SilentlyContinue } else { $null }
if (-not $proc) {
    Write-Host '見張り役は動いていません'
    exit 0
}
if ($Now) {
    # stop.ps1 と同じ（走っているランを止めて Play を抜け、まとめを書いてから終わる）
    & (Join-Path $here 'stop.ps1')
    exit 0
}
Set-Content -Path (Join-Path $ipc 'pause.flag') -Value 'pause' -Encoding ascii
Write-Host "一時停止の指示を出しました。走っているランが終わったら止まります（見張り役 pid $runnerPid）。続きは resume.ps1"
