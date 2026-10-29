$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testExecutable = Join-Path $repositoryRoot 'tests\WebBlocker.Shared.Tests\bin\Release\net48\WebBlocker.Shared.Tests.exe'
$proxyHost = Join-Path $repositoryRoot 'src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe'

dotnet build (Join-Path $repositoryRoot 'NetWard.sln') --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed; policy tests were not run.'
}

if (-not (Test-Path -LiteralPath $testExecutable)) {
    throw "Test executable was not produced: $testExecutable"
}

& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw 'Domain policy tests failed.'
}

& $proxyHost --help
if ($LASTEXITCODE -ne 0) {
    throw 'The proxy host help smoke test failed.'
}

$duplicateRuleCommand = '"' + $proxyHost + '" --mode allowlist --rule exact:example.com --rule exact:EXAMPLE.COM. 2>&1'
$duplicateRuleOutput = & $env:ComSpec /d /c $duplicateRuleCommand
$duplicateRuleExitCode = $LASTEXITCODE
if ($duplicateRuleExitCode -ne 2 -or ($duplicateRuleOutput -join "`n") -notmatch 'Duplicate normalized domain rule') {
    throw 'The proxy host invalid-configuration smoke test failed.'
}

Write-Output 'Proxy host CLI smoke tests passed.'
