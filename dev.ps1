<#
.SYNOPSIS
  One-command local dev stack: moto (local S3/DynamoDB) + Aspire Dashboard in Docker, and
  CatalogService / PictureService / Web as detached background processes, seeded with fixtures.

.DESCRIPTION
  ./dev.ps1 up          start everything (seeds moto + the local Web database when they are empty)
  ./dev.ps1 down        stop the three services and the moto/Aspire containers
  ./dev.ps1 status      show which services and containers are running
  ./dev.ps1 logs <svc>  show the last log lines of catalog | picture | web  (-Follow to tail)
  ./dev.ps1 reset       wipe and reseed moto and the local Web database, then restart the services

  Logs: .dev/logs/<svc>.log   PIDs: .dev/pids/<svc>   Local Web database: .dev/web/users.db
  Web: http://localhost:5080 (users alice / bob, password in testdata/manifest.json)
  Aspire Dashboard: http://localhost:18888

  Safety: refuses to start if AWS_PROFILE / AWS credentials are set in this shell, and always points
  PictureService at the local moto server with dummy credentials, so it can never reach prod AWS.
  Gemini is opt-in: set GEMINI_API_KEY (shell, or picture_service/.env) to enable photo analysis.
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'status', 'logs', 'reset')]
    [string]$Command = 'status',

    [Parameter(Position = 1)]
    [ValidateSet('catalog', 'picture', 'web')]
    [string]$Service,

    [switch]$Follow
)

# Not 'Stop': Windows PowerShell 5.1 turns any native-command stderr output (docker compose's
# progress lines) into a terminating error. Native commands are checked via $LASTEXITCODE and
# failures are raised explicitly with `throw`.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$devDir = Join-Path $root '.dev'
$logDir = Join-Path $devDir 'logs'
$pidDir = Join-Path $devDir 'pids'
$webDbPath = Join-Path $devDir 'web\users.db'

$motoUrl = 'http://localhost:5000'
$otlpUrl = 'http://localhost:18890'
$bucketName = 'ninjago-local-photos'
$tableName = 'ninjago-local-sidecars'
$region = 'eu-central-1'

# name -> port. Picture uses 8090 because 8080 is commonly taken by other local Docker containers.
$services = [ordered]@{ catalog = 5073; picture = 8090; web = 5080 }

function Assert-NoAwsConfiguration {
    $offending = @('AWS_PROFILE', 'AWS_ACCESS_KEY_ID', 'AWS_SESSION_TOKEN') |
        Where-Object { [Environment]::GetEnvironmentVariable($_) }
    if ($offending) {
        throw ("Refusing to start: $($offending -join ', ') is set in this shell. The local stack must only talk to " +
            "moto. Open a shell without AWS configuration (or Remove-Item Env:AWS_PROFILE etc.) and retry.")
    }
}

function Invoke-WithEnvironment {
    # Loop variables are deliberately not called $name/$value: PowerShell scoping is dynamic and
    # case-insensitive, so they would shadow the caller's $Name inside the invoked scriptblock.
    param([hashtable]$Variables, [scriptblock]$Action)
    $saved = @{}
    foreach ($variableName in $Variables.Keys) { $saved[$variableName] = [Environment]::GetEnvironmentVariable($variableName) }
    try {
        foreach ($variableName in $Variables.Keys) { [Environment]::SetEnvironmentVariable($variableName, $Variables[$variableName]) }
        & $Action
    }
    finally {
        foreach ($variableName in $saved.Keys) { [Environment]::SetEnvironmentVariable($variableName, $saved[$variableName]) }
    }
}

function Get-LocalAwsEnvironment {
    @{
        AWS_ENDPOINT_URL      = $motoUrl
        AWS_ACCESS_KEY_ID     = 'test'
        AWS_SECRET_ACCESS_KEY = 'test'
        AWS_REGION            = $region
        AWS_PROFILE           = $null
        PHOTOS_BUCKET_NAME    = $bucketName
        SIDECAR_TABLE_NAME    = $tableName
        VIRTUAL_ENV           = $null
    }
}

function Get-GeminiEnvironment {
    # Opt-in: shell value wins, else GEMINI_* lines from picture_service/.env (values never printed).
    $result = @{}
    $envFile = Join-Path $root 'picture_service\.env'
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match '^\s*(GEMINI_[A-Z_]+)\s*=\s*(.*?)\s*$') {
                $result[$Matches[1]] = $Matches[2].Trim('"').Trim("'")
            }
        }
    }
    foreach ($name in @('GEMINI_API_KEY', 'GEMINI_MODEL')) {
        $shellValue = [Environment]::GetEnvironmentVariable($name)
        if ($shellValue) { $result[$name] = $shellValue }
    }
    $result
}

function Test-PortOpen {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Wait-ForPort {
    param([string]$Name, [int]$Port, [int]$TimeoutSeconds = 90)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PortOpen $Port) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "$Name did not open port $Port within ${TimeoutSeconds}s - see .dev/logs."
}

function Get-ServicePid {
    param([string]$Name)
    $file = Join-Path $pidDir $Name
    if (-not (Test-Path $file)) { return $null }
    $value = [int](Get-Content $file -TotalCount 1)
    if (Get-Process -Id $value -ErrorAction SilentlyContinue) { return $value }
    Remove-Item $file -Force
    return $null
}

function Stop-Services {
    foreach ($name in $services.Keys) {
        $processId = Get-ServicePid $name
        if ($processId) {
            & taskkill.exe /PID $processId /T /F | Out-Null
            Write-Host "stopped $name (pid $processId)"
        }
        Remove-Item (Join-Path $pidDir $name) -Force -ErrorAction SilentlyContinue
    }
}

function Merge-Environment {
    # Later hashtables win on duplicate keys (a plain `+` throws on duplicates).
    $merged = @{}
    foreach ($table in $args) { foreach ($key in $table.Keys) { $merged[$key] = $table[$key] } }
    $merged
}

function Start-DevService {
    param([string]$Name, [string]$Executable, [string]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment)
    $log = Join-Path $logDir "$Name.log"
    $cmdLine = "/d /s /c `"`"$Executable`" $Arguments > `"$log`" 2>&1`""
    Invoke-WithEnvironment $Environment {
        $process = Start-Process -FilePath 'cmd.exe' -ArgumentList $cmdLine -WorkingDirectory $WorkingDirectory `
            -WindowStyle Hidden -PassThru
        Set-Content -Path (Join-Path $pidDir $Name) -Value $process.Id
    }
    Write-Host "started $Name -> .dev/logs/$Name.log"
}

function Test-MotoSeeded {
    try {
        Invoke-WebRequest -Uri "$motoUrl/$bucketName" -Method Head -UseBasicParsing | Out-Null
        return $true
    }
    catch { return $false }
}

function Invoke-Up {
    param([switch]$ForceSeed)

    Assert-NoAwsConfiguration
    New-Item -ItemType Directory -Force $logDir, $pidDir, (Split-Path $webDbPath) | Out-Null
    Stop-Services

    foreach ($serviceName in $services.Keys) {
        if (Test-PortOpen $services[$serviceName]) {
            throw ("Port $($services[$serviceName]) ($serviceName) is already in use by a process dev.ps1 did not start. " +
                'Stop it (e.g. a VS Code debug session or an old dotnet/python process) and retry.')
        }
    }

    Write-Host '== containers (moto + Aspire Dashboard)'
    & docker compose up -d moto aspire-dashboard 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed - is Docker running?' }
    Wait-ForPort 'moto' 5000 30

    Write-Host '== build'
    & dotnet build (Join-Path $root 'NinjagoScanner.CatalogService\NinjagoScanner.CatalogService.csproj') -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'CatalogService build failed.' }
    & dotnet build (Join-Path $root 'NinjagoScanner.Web\NinjagoScanner.Web.csproj') -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
    Push-Location (Join-Path $root 'picture_service')
    try {
        Invoke-WithEnvironment @{ VIRTUAL_ENV = $null } {
            & uv sync --quiet
            if ($LASTEXITCODE -ne 0) { throw 'uv sync failed.' }
            & uv run python scripts/gen_proto.py | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'proto generation failed.' }
        }
    }
    finally { Pop-Location }

    Write-Host '== seed'
    if ($ForceSeed -or -not (Test-MotoSeeded)) {
        Invoke-WithEnvironment (Get-LocalAwsEnvironment) {
            Push-Location (Join-Path $root 'picture_service')
            try {
                & uv run python ../scripts/seed_moto.py
                if ($LASTEXITCODE -ne 0) { throw 'moto seeding failed.' }
            }
            finally { Pop-Location }
        }
    }
    else { Write-Host 'moto already seeded (use ./dev.ps1 reset to reseed)' }

    if ($ForceSeed) {
        foreach ($suffix in '', '-shm', '-wal') { Remove-Item "$webDbPath$suffix" -Force -ErrorAction SilentlyContinue }
    }
    if (-not (Test-Path $webDbPath)) {
        $manifest = Join-Path $root 'testdata\manifest.json'
        Invoke-WithEnvironment @{ AUTH_DATABASE_PATH = $webDbPath; SEED_MANIFEST_PATH = $manifest; Logging__LogLevel__Default = 'Warning' } {
            Push-Location (Join-Path $root 'NinjagoScanner.Web')
            try {
                & dotnet run --no-build -- seed
                if ($LASTEXITCODE -ne 0) { throw 'Web database seeding failed.' }
            }
            finally { Pop-Location }
        }
    }
    else { Write-Host 'Web database already seeded (use ./dev.ps1 reset to reseed)' }

    Write-Host '== services'
    $common = @{ OTEL_EXPORTER_OTLP_ENDPOINT = $otlpUrl; AWS_PROFILE = $null; VIRTUAL_ENV = $null }
    $dotnetExe = (Get-Command dotnet).Source

    Start-DevService 'catalog' $dotnetExe 'NinjagoScanner.CatalogService.dll' `
        (Join-Path $root 'NinjagoScanner.CatalogService\bin\Debug\net10.0') `
        (Merge-Environment $common @{ ASPNETCORE_URLS = 'http://localhost:5073'; ASPNETCORE_ENVIRONMENT = 'Development' })
    Wait-ForPort 'catalog' $services.catalog

    $pictureEnv = Merge-Environment $common (Get-LocalAwsEnvironment) (Get-GeminiEnvironment) @{
        PORT                    = "$($services.picture)"
        PYTHONPATH              = (Join-Path $root 'picture_service\src')
        PYTHONUNBUFFERED        = '1'
        CATALOG_SERVICE_ADDRESS = 'http://localhost:5073'
    }
    Start-DevService 'picture' (Join-Path $root 'picture_service\.venv\Scripts\python.exe') '-m picture_service.main' `
        (Join-Path $root 'picture_service') $pictureEnv
    Wait-ForPort 'picture' $services.picture

    $webEnv = Merge-Environment $common @{
        ASPNETCORE_URLS         = 'http://localhost:5080'
        ASPNETCORE_ENVIRONMENT  = 'Development'
        AUTH_DATABASE_PATH      = $webDbPath
        CATALOG_SERVICE_ADDRESS = 'http://localhost:5073'
        PICTURE_SERVICE_ADDRESS = "http://localhost:$($services.picture)"
    }
    # Web's content root is its working directory and must be the project folder (wwwroot, static
    # web assets), while the dll lives in bin - hence the relative dll path.
    Start-DevService 'web' $dotnetExe 'bin\Debug\net10.0\NinjagoScanner.Web.dll' (Join-Path $root 'NinjagoScanner.Web') $webEnv
    Wait-ForPort 'web' $services.web

    Write-Host ''
    Write-Host 'Local stack is up:'
    Write-Host '  Web              http://localhost:5080   (alice / bob, password: see testdata/manifest.json)'
    Write-Host '  Aspire Dashboard http://localhost:18888'
    if (-not (Get-GeminiEnvironment).ContainsKey('GEMINI_API_KEY')) {
        Write-Host '  Note: no GEMINI_API_KEY - uploading new photos will fail analysis (seeded photos work).'
    }
}

function Show-Status {
    foreach ($name in $services.Keys) {
        $processId = Get-ServicePid $name
        $port = $services[$name]
        $state = if ($processId) { "running (pid $processId)" } else { 'stopped' }
        $listening = if (Test-PortOpen $port) { "port $port open" } else { "port $port closed" }
        '{0,-9} {1,-24} {2}' -f $name, $state, $listening
    }
    foreach ($container in @('moto', 'aspire-dashboard')) {
        $running = & docker compose ps --status running --services 2>$null | Where-Object { $_ -eq $container }
        '{0,-9} {1}' -f $container, $(if ($running) { 'running' } else { 'stopped' })
    }
}

Set-Location $root

switch ($Command) {
    'up' { Invoke-Up }
    'down' {
        Stop-Services
        & docker compose stop moto aspire-dashboard 2>&1 | ForEach-Object { "$_" }
    }
    'status' { Show-Status }
    'logs' {
        if (-not $Service) { throw 'Usage: ./dev.ps1 logs <catalog|picture|web> [-Follow]' }
        $log = Join-Path $logDir "$Service.log"
        if (-not (Test-Path $log)) { throw "No log yet: $log" }
        if ($Follow) { Get-Content $log -Tail 100 -Wait } else { Get-Content $log -Tail 100 }
    }
    'reset' {
        Stop-Services
        Invoke-Up -ForceSeed
    }
}
