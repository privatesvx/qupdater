@echo off
echo Building qUpdater...

:: Clean previous builds
if exist "bin" rd /s /q "bin"
if exist "obj" rd /s /q "obj"

:: Build and publish
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

:: Copy the executable to the root directory
echo.
echo Copying executable to root directory...
copy /Y "bin\Release\net9.0-windows\win-x64\publish\qUpdater.exe" "qUpdater.exe"

echo.
echo Build complete! The executable is: qUpdater.exe
echo.
pause