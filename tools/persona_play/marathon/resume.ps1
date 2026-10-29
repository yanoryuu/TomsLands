# 一時停止した人格マラソンを、前回と同じ設定ファイル・同じセーブ（Library\PersonaMarathonSaves\<人格>\main）の続きから始める。
#   powershell -ExecutionPolicy Bypass -File tools/persona_play/marathon/resume.ps1
#   -Config <ファイル>: 別の設定で続ける
param([string]$Config = "")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = (Resolve-Path (Join-Path $here '..\..\..')).Path
$ipc = Join-Path $project 'Library\PersonaMarathon'
if (-not $Config) {
    $last = Join-Path $ipc 'last_config.txt'
    if (Test-Path $last) { $Config = (Get-Content $last -Encoding utf8 | Select-Object -First 1).Trim() }
}
Remove-Item (Join-Path $ipc 'pause.flag') -ErrorAction SilentlyContinue
if ($Config) { Write-Host "設定: $Config" } else { Write-Host '設定ファイルなし（既定値）' }
& (Join-Path $here 'start.ps1') -Config $Config
