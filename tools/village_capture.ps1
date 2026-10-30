# 村画面（VillageScene）の見た目を 1920x1080 の PNG に書き出す（Unity バッチモード・ウィンドウなし）。
#   powershell -ExecutionPolicy Bypass -File tools/village_capture.ps1 [-Shots hud,invest,conversion,overview] [-Levels 0,3,5] [-Ranks 0,1,2,3] [-Funds 99999] [-Out <dir>]
# -Ranks: 村の格ごとの地面と装飾（VillageRankDecor.prefab）を一時的に置き、施設レベルも格に合わせて撮る（village_rank{格}_{種類}.png）。
#         -Ranks だけ付けたときはレベル別（-Levels）は撮らない。両方付ければ両方撮る。
# -Shots: hud / invest / conversion / rankup（格上げのポップ）/ record（自己ベストの掲示板）/ overview
# -Funds: HUD の村資金（投資パネルの費用と比べる額）。多めにすると「足りる＝金色・ボタン有効」の状態が撮れる。
# 中身は Assets/Scripts/Editor/VillageCapture.cs（編集モードで開いて撮るだけ。シーンもセーブも書き換えない）。
# 描画が要るので -nographics は付けない。同じプロジェクトをエディタで開いているとロックで失敗する。
param(
    [string]$Shots = "hud,invest,conversion,overview",
    [string]$Levels = "0,3,5",
    [string]$Ranks = "",
    [int]$Funds = -1,
    [string]$Out = "",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = (Resolve-Path (Join-Path $here '..')).Path

# 既定の出力先は C ドライブの外（_scratch_wt04）
if (-not $Out) {
    $Out = Join-Path 'D:\WorkTree\TomsLands\_scratch_wt04\village_capture' (Get-Date -Format 'yyyyMMdd_HHmmss')
}
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out 'unity.log'

$argList = @(
    '-batchmode',
    '-projectPath', "`"$project`"",
    '-executeMethod', 'VillageCapture.RunBatch',
    '-logFile', "`"$log`"",
    '-captureOut', "`"$Out`"",
    '-captureShots', $Shots
)
if (-not $Ranks -or $PSBoundParameters.ContainsKey('Levels')) { $argList += @('-captureLevels', $Levels) }
if ($Ranks) { $argList += @('-captureRanks', $Ranks) }
if ($Funds -ge 0) { $argList += @('-captureFunds', $Funds) }
Write-Host "撮影中… ($Shots / Lv $Levels / 格 $Ranks)"
$p = Start-Process -FilePath $Unity -ArgumentList $argList -Wait -PassThru -WindowStyle Hidden
if ($p.ExitCode -ne 0) {
    Write-Host "失敗しました（終了コード $($p.ExitCode)）。ログ: $log"
    Select-String -Path $log -Pattern '\[VillageCapture\]|error CS|another Unity instance' | ForEach-Object { $_.Line }
    exit $p.ExitCode
}
Select-String -Path $log -Pattern '\[VillageCapture\]' | ForEach-Object { $_.Line }
Write-Host "出力: $Out"
