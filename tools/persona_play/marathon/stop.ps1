# 人格マラソンを止める。走っているランは止めて Play を抜け、Ollama を GPU に戻し、まとめを書いてから見張り役が終わる。
#   powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/stop.ps1
#   -Force: 見張り役をすぐ終わらせ、エディタにも止める指示だけ出す（まとめ・Ollama の戻しは行わない）
param([switch]$Force)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = (Resolve-Path (Join-Path $here '..\..\..')).Path
$ipc = Join-Path $project 'Library\PersonaMarathon'
New-Item -ItemType Directory -Force $ipc | Out-Null
$pidFile = Join-Path $ipc 'runner.pid'
$runnerPid = if (Test-Path $pidFile) { [int](Get-Content $pidFile) } else { 0 }
$proc = if ($runnerPid) { Get-Process -Id $runnerPid -ErrorAction SilentlyContinue } else { $null }

if ($Force) {
    if ($proc) { Stop-Process -Id $runnerPid -Force; Write-Host "見張り役（pid $runnerPid）を終わらせました" }
    Set-Content -Path (Join-Path $ipc 'stop.flag') -Value 'stop' -Encoding ascii
    Remove-Item (Join-Path $ipc 'command.json') -ErrorAction SilentlyContinue
    Write-Host 'エディタに止める指示（stop.flag）を出しました'
    exit 0
}

if (-not $proc) {
    Write-Host '見張り役は動いていません'
    exit 0
}
Set-Content -Path (Join-Path $ipc 'runner_stop.flag') -Value 'stop' -Encoding ascii
Write-Host "止める指示を出しました。見張り役（pid $runnerPid）が終わるのを待ちます（最大 10 分）..."
if (Wait-Process -Id $runnerPid -Timeout 600 -ErrorAction SilentlyContinue) { }
if (Get-Process -Id $runnerPid -ErrorAction SilentlyContinue) {
    Write-Host 'まだ終わっていません。すぐ止めるなら stop.ps1 -Force'
} else {
    Write-Host '止まりました。Recordings\marathon_<日時>\summary.md を見てください'
}
