@echo off
echo Starting SOSDM (Development Mode)...
set SOSDM_LOG_LEVEL=Debug
cd /d "src\bin\Release\net6.0"
SOSDM.exe
pause
