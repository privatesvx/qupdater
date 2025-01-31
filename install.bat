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

echo.
echo Current directory: %CD%

:: Kill any existing instances
echo Stopping any running instances...
taskkill /F /IM qUpdater.exe 2>nul

:: Create program directory
set INSTALL_DIR=%ProgramFiles%\qUpdater
echo Creating installation directory: %INSTALL_DIR%
mkdir "%INSTALL_DIR%" 2>nul

:: Copy files
echo Copying files...
if not exist "qUpdater.exe" (
    echo Error: qUpdater.exe not found in current directory!
    echo Current directory: %CD%
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

copy /Y "qUpdater.exe" "%INSTALL_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to copy qUpdater.exe to %INSTALL_DIR%
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

:: Add to registry for startup (HKCU only)
echo Adding to startup registry...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "qUpdater" /t REG_SZ /d "\"%INSTALL_DIR%\qUpdater.exe\"" /f
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to add registry entry
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

:: Verify registry entry
echo Verifying registry entry...
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "qUpdater"
if %ERRORLEVEL% NEQ 0 (
    echo Error: Failed to verify registry entry
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

:: Verify file exists in correct location
if not exist "%INSTALL_DIR%\qUpdater.exe" (
    echo Error: qUpdater.exe not found in installation directory!
    echo Expected location: %INSTALL_DIR%\qUpdater.exe
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

echo.
echo Installation complete!
echo Installation directory: %INSTALL_DIR%
echo Starting qUpdater...
start "" "%INSTALL_DIR%\qUpdater.exe"

echo.
echo Press any key to close this window...
pause >nul