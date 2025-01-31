@echo off
echo Installing qUpdater...

:: Request admin privileges
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Requesting administrative privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    if exist "%temp%\getadmin.vbs" ( del "%temp%\getadmin.vbs" )
    pushd "%CD%"
    CD /D "%~dp0"

:: Kill any existing instances
taskkill /F /IM qUpdater.exe 2>nul

:: Create program directory
set INSTALL_DIR=%ProgramFiles%\qUpdater
mkdir "%INSTALL_DIR%" 2>nul

:: Copy files
copy /Y "qUpdater.exe" "%INSTALL_DIR%"
:: Add to registry for startup
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "qUpdater" /t REG_SZ /d "\"%INSTALL_DIR%\qUpdater.exe\"" /f

echo Installation complete!
start "" "%INSTALL_DIR%\qUpdater.exe"