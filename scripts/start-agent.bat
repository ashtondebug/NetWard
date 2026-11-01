@echo off
setlocal
set ROOT=%~dp0..
set AGENT=%ROOT%\src\WebBlocker.Agent\bin\Release\net48\NetWard.Agent.exe
if not exist "%AGENT%" (
  echo 未找到被控端程序：%AGENT%
  echo 请先运行 scripts\build.ps1 构建项目。
  pause
  exit /b 1
)
start "" "%AGENT%" %*