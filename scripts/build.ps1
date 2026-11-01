$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Write-Output "NetWard: 正在构建 $repositoryRoot ..."
dotnet build (Join-Path $repositoryRoot 'NetWard.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
Write-Output ''
Write-Output '构建完成。可执行文件：'
Write-Output ('  管理端  ' + (Join-Path $repositoryRoot 'src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe'))
Write-Output ('  被控端  ' + (Join-Path $repositoryRoot 'src\WebBlocker.Agent\bin\Release\net48\NetWard.Agent.exe'))
Write-Output ('  代理宿主 ' + (Join-Path $repositoryRoot 'src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe'))