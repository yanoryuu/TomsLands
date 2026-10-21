@echo off
chcp 65001 >nul
rem トコのトムトムランド 体験版を Steam にアップロードする（まだ使わない: 体験版の App ID が未取得）。
rem 使い方: run_tomslands_demo_build.bat <Steamworks のアカウント名>
rem パスワードと Steam Guard のコードは steamcmd がその場で聞いてくる（ここには保存しない）。
rem このファイルと scripts\ を D:\Tools\steamworks_sdk_165\sdk\tools\ContentBuilder\ に置いて使う。
if "%~1"=="" (
  echo 使い方: run_tomslands_demo_build.bat ^<アカウント名^>
  exit /b 1
)
findstr /R /C:"\"AppID\" \"0\"" "%~dp0scripts\app_build_demo.vdf" >nul
if not errorlevel 1 (
  echo scripts\app_build_demo.vdf の AppID が仮の 0 のままです。体験版の App ID と depot の番号を書き換えてください。
  exit /b 1
)
findstr /R /C:"\"DepotID\" \"0\"" "%~dp0scripts\depot_build_demo.vdf" >nul
if not errorlevel 1 (
  echo scripts\depot_build_demo.vdf の DepotID が仮の 0 のままです。
  exit /b 1
)
if not exist "D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe" (
  echo D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe がありません。先に Unity の Tools/TomsLands/体験版/体験版をビルド を実行してください。
  exit /b 1
)
cd /d "%~dp0builder"
steamcmd.exe +login %1 +run_app_build "%~dp0scripts\app_build_demo.vdf" +quit
