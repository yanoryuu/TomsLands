@echo off
rem Upload the TomsLands demo (App 5186790 / depot 5186791) to Steam.
rem Usage: double-click, or run_tomslands_demo_build.bat ACCOUNT_NAME
rem steamcmd asks for the password and Steam Guard code itself (nothing is saved here).
rem Put this file and scripts\ in D:\Tools\steamworks_sdk_165\sdk\tools\ContentBuilder\
rem ASCII only on purpose: cmd.exe misparses UTF-8 Japanese inside if-blocks.
setlocal
set "ACCOUNT=%~1"
if "%ACCOUNT%"=="" set /p "ACCOUNT=Steamworks account name: "
if "%ACCOUNT%"=="" (
  echo No account name. Stopped.
  goto :end
)
findstr /C:"\"AppID\" \"0\"" "%~dp0scripts\app_build_demo.vdf" >nul
if not errorlevel 1 (
  echo scripts\app_build_demo.vdf still has AppID 0. Stopped.
  goto :end
)
if not exist "D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe" (
  echo D:\UnityGame\TomsShop_Demo\TomSLand_Demo.exe not found. Build the demo first. Stopped.
  goto :end
)
echo Uploading D:\UnityGame\TomsShop_Demo to App 5186790 as %ACCOUNT% ...
cd /d "%~dp0builder"
steamcmd.exe +login %ACCOUNT% +run_app_build "%~dp0scripts\app_build_demo.vdf" +quit
echo steamcmd finished with code %errorlevel%.
:end
echo.
pause
endlocal
