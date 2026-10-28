@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==========================================================
echo   VaultGuard Software Suite - Build Script
echo   (C) 2026 Example Technology Co., Ltd.
echo ==========================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo         Please install .NET SDK 8.0 or later:
    echo         https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version') do set SDKVER=%%v
echo [OK] .NET SDK version: %SDKVER%
echo.

echo [STEP 1/2] Building solution (Release) ...
dotnet build VaultGuard.sln -c Release --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. See messages above.
    pause
    exit /b 1
)
echo [OK] Build succeeded.
echo.

echo [STEP 2/2] Publishing self-contained executables to dist\ ...
if exist dist rmdir /s /q dist

dotnet publish src\VaultGuard.Backup.Web\VaultGuard.Backup.Web.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist\BackupWeb --nologo
if errorlevel 1 goto :failed

dotnet publish src\VaultGuard.Endpoint.Server\VaultGuard.Endpoint.Server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist\EndpointServer --nologo
if errorlevel 1 goto :failed

dotnet publish src\VaultGuard.Endpoint.Agent\VaultGuard.Endpoint.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist\EndpointAgent --nologo
if errorlevel 1 goto :failed

echo.
echo ==========================================================
echo   BUILD SUCCEEDED
echo ==========================================================
echo   dist\BackupWeb\VaultGuard.Backup.Web.exe
echo   dist\EndpointServer\VaultGuard.Endpoint.Server.exe
echo   dist\EndpointAgent\VaultGuard.Endpoint.Agent.exe
echo.
echo   These executables are self-contained: the target machine
echo   does NOT need .NET runtime installed.
echo ==========================================================
echo.
pause
exit /b 0

:failed
echo.
echo ==========================================================
echo   [ERROR] Publish failed. See messages above.
echo ==========================================================
echo.
echo   The most common cause of a publish failure is a mismatch
echo   between your .NET SDK version and the runtime pack.
echo   Self-contained publishing needs the runtime pack that
echo   exactly matches your SDK.
echo.
echo   Version mapping (check yours with: dotnet --version):
echo     SDK 8.0.1xx / 8.0.2xx  -^>  runtime 8.0.2
echo     SDK 8.0.3xx / 8.0.4xx  -^>  runtime 8.0.3x
echo.
echo   Fix: make sure nuget.org is reachable (the SDK downloads the
echo   matching runtime pack automatically), then run this script again.
echo.
echo   Note: STEP 1 (compiling the source) has already succeeded.
echo   If you only need a runnable program, use "dotnet run" instead
echo   of a self-contained publish (see README, method B).
echo ==========================================================
echo.
pause
exit /b 1
