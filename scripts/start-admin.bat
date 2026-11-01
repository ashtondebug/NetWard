@echo off
setlocal
set ROOT=%~dp0..
set ADMIN=%ROOT%\src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe
if not exist "%ADMIN%" (
  echo 未找到管理端程序：%ADMIN%
  echo 请先运行 scripts\build.ps1 构建项目。
  pause
  exit /b 1
)
start "" "%ADMIN%" %*