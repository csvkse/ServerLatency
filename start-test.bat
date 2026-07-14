@echo off
title ServerLatency One-Click Test

:MENU
cls
echo ===================================================
echo   ServerLatency - Local Test Environment Startup
echo ===================================================
echo.

echo [0/2] Cleaning up previous instances (Restarting)...
taskkill /F /FI "WINDOWTITLE eq ServerLatency - Server*" /T >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq ServerLatency - Node*" /T >nul 2>&1
taskkill /F /IM ServerLatency.exe /T >nul 2>&1
timeout /t 1 /nobreak > nul

echo [1/2] Starting ServerLatency Control Plane (Server)...
start "ServerLatency - Server" cmd /k "dotnet run --project ServerLatency\ServerLatency.csproj -- -m Server -p 15002 -k LocalTestKey123"

echo Waiting 3 seconds for server to initialize...
timeout /t 3 /nobreak > nul

echo [2/2] Starting ServerLatency Edge Node (Client)...
start "ServerLatency - Node" cmd /k "dotnet run --project ServerLatency\ServerLatency.csproj -- -m Client --ServerUrl http://localhost:15002 -k LocalTestKey123 -n LocalTestNode"

echo.
echo ===================================================
echo   Done! Server and Node are running in new windows.
echo   You can open your browser and visit:
echo   http://localhost:15002/index.html?key=LocalTestKey123
echo ===================================================
echo.
echo   [!] Press ANY KEY in this window to RESTART all.
echo   [!] Close this window to exit completely.
echo ===================================================
pause > nul
goto MENU
