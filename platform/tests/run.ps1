# Requires local PostgreSQL compose and Node 24. Tests create a private empty database.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
$dbName = 'krylya_test_' + [guid]::NewGuid().ToString('N')
$dc = @('compose','-f','compose.yml','-p','krylya-platform','exec','-T','postgres')
$created = $false
$server = $null
try {
    dotnet build Krylya.Api -c Release
    $dll = (Resolve-Path 'Krylya.Api/bin/Release/net10.0/Krylya.Api.dll').Path
    $dbPass = ((Get-Content .env | Where-Object { $_ -like 'KRYLYA_DB_PASSWORD=*' }) -split '=',2)[1]
    docker @dc createdb -U krylya -T template0 $dbName
    $created = $true
    $env:ConnectionStrings__Krylya = "Host=127.0.0.1;Port=54329;Username=krylya;Database=$dbName;Password=$dbPass"
    $env:Bootstrap__Password = [guid]::NewGuid().ToString('N')
    foreach ($role in 'admin','coach') {
        $env:Bootstrap__Login = "test-$role"
        dotnet $dll "--bootstrap-$role"
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $server = Start-Process dotnet -ArgumentList $dll,'--urls','http://127.0.0.1:5096' -PassThru -WindowStyle Hidden
    foreach ($i in 1..40) {
        if ($server.HasExited) { throw 'Test server exited' }
        try { Invoke-RestMethod 'http://127.0.0.1:5096/health' -TimeoutSec 2 -NoProxy | Out-Null; break }
        catch { Start-Sleep -Milliseconds 250 }
    }
    Invoke-WebRequest http://127.0.0.1:5096/health -NoProxy | Out-Null
    $env:TEST_API_DLL = $dll
    node tests/operations.mjs
} finally {
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
    if ($created) { docker @dc dropdb -U krylya $dbName }
    Pop-Location
}
