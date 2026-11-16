@echo off
setlocal
title Build YTC_DeathStats

rem Game folder: use env HK_DIR if set, else the default Steam path.
rem NOTE: keep this file pure ASCII -- cmd.exe reads .bat as GBK on zh-CN Windows.
if "%HK_DIR%"=="" set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight"
if not "%HK_DIR%"=="" set "GAME=%HK_DIR%"

set "MANAGED=%GAME%\hollow_knight_Data\Managed"
set "CORE=%GAME%\BepInEx\core"
set "SRC=%~dp0"
set "ROOT=%SRC%.."
set "OUT=%ROOT%\out"
set "LOG=%OUT%\build.log"
set "DLL=YTC_DeathStats.dll"

rem Compiler: local Roslyn first (supports latest C#), else the framework csc.
set "CSCPATH=%ROOT%\tools\roslyn\tasks\net472\csc.exe"
if not exist "%CSCPATH%" set "CSCPATH=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSCPATH%" set "CSCPATH=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%GAME%\hollow_knight_Data" (
  echo.
  echo   [ERROR] Game folder not found: "%GAME%"
  echo   Set it first, e.g.  set HK_DIR=D:\SteamLibrary\steamapps\common\Hollow Knight
  echo.
  pause
  exit /b 1
)

if not exist "%CSCPATH%" (
  echo.
  echo   [ERROR] csc.exe not found. Install .NET Framework 4.7.2+ or put
  echo           Roslyn at tools\roslyn\tasks\net472\csc.exe
  echo.
  pause
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"
if not exist "%GAME%\BepInEx\plugins" mkdir "%GAME%\BepInEx\plugins"

echo ================ BUILD LOG ================ > "%LOG%"
echo [1/3] Compiling DeathStats.cs ... >> "%LOG%"

"%CSCPATH%" /nologo /target:library /platform:anycpu /codepage:65001 /optimize+ /langversion:latest /nowarn:1701,1702,0618,1705 /out:"%OUT%\%DLL%" /r:"%MANAGED%\UnityEngine.dll" /r:"%MANAGED%\UnityEngine.CoreModule.dll" /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" /r:"%MANAGED%\UnityEngine.UIModule.dll" /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" /r:"%MANAGED%\UnityEngine.Physics2DModule.dll" /r:"%MANAGED%\UnityEngine.UI.dll" /r:"%MANAGED%\netstandard.dll" /r:"%MANAGED%\System.Runtime.dll" /r:"%CORE%\BepInEx.dll" /r:"%CORE%\0Harmony.dll" "%SRC%DeathStats.cs" >> "%LOG%" 2>&1

if errorlevel 1 goto fail

echo [2/3] Removing old DLLs (XiaoCanDeathStats.dll / DeathStats.dll) ... >> "%LOG%"
if exist "%GAME%\BepInEx\plugins\XiaoCanDeathStats.dll" del /q "%GAME%\BepInEx\plugins\XiaoCanDeathStats.dll" >> "%LOG%" 2>&1
if exist "%GAME%\BepInEx\plugins\DeathStats.dll" del /q "%GAME%\BepInEx\plugins\DeathStats.dll" >> "%LOG%" 2>&1

echo [3/3] Deploying %DLL% to BepInEx\plugins ... >> "%LOG%"
copy /y "%OUT%\%DLL%" "%GAME%\BepInEx\plugins\%DLL%" >> "%LOG%" 2>&1
if errorlevel 1 goto fail

echo BUILD RESULT: SUCCESS >> "%LOG%"
echo.
echo   ============================================
echo     BUILD OK - mod installed
echo     %DLL%  -^>  BepInEx\plugins
echo   ============================================
echo.
echo   Next: launch the game once, then come back.
echo.
pause
exit /b 0

:fail
echo BUILD RESULT: FAILED >> "%LOG%"
echo.
echo   ============================================
echo     BUILD FAILED - error log below
echo   ============================================
echo.
type "%LOG%"
echo.
pause
exit /b 1
