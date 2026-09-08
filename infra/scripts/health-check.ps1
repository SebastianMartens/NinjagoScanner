<#
.SYNOPSIS
    Health-checks NinjagoScanner's AWS storage and Fly.io compute and reports a single pass/fail view.

.DESCRIPTION
    As of the aws-compute-teardown change, infra/ is storage-only (see infra/README.md) — there is
    no ECS/Fargate, internal NLB, BFF Lambda, web-client S3 bucket, or CloudFront distribution.
    Compute runs on Fly.io instead (fly-hosting-migration), managed by flyctl/fly.toml rather than
    Terraform — see infra/README.md's "AWS compute (what's not here) vs. Fly.io compute" section.
    This script checks:
      - the DynamoDB sidecar table(s) — both the live collection-scoped table and, informationally,
        the legacy table kept around post-migration (see add-collection-data-isolation/design.md)
      - the S3 photo bucket
      - that PictureService's own scoped IAM user (modules/iam-user — GetItem/PutItem/DeleteItem/
        Scan/Query on the live sidecar table, GetObject/PutObject/DeleteObject/ListBucket on the
        photos/* prefix only, nothing else) can actually read both, using its real credentials
        rather than the operator's ambient ones. describe-table/head-bucket above only prove the
        resources exist to whoever is running this script; they don't catch a tightened bucket
        policy or table policy that quietly breaks the app itself.
      - each of the three Fly apps' machine/health-check state, via `flyctl status --json`
      - an end-to-end HTTP check against NinjagoScanner.Web's public login page — the only page
        reachable without authentication and the only one of the three apps with a public Fly IP
        (CatalogService/PictureService are reachable only over Fly's private network, so there's no
        public HTTP endpoint on them to check from outside Fly)

    Resource names are deterministic (see infra/modules/*/main.tf and each project's fly.toml).

    Requires the AWS CLI (`aws`) on PATH, with a local named profile (see AwsProfile below, default
    `terraform` per infra/README.md's "First apply, with your own AWS credentials") that can read
    these services (the same account/region infra/environments/prod deploys into), and flyctl on
    PATH, authenticated against the Fly org these apps run in. Either CLI's absence degrades that
    section to a WARN rather than failing the whole script. The scoped-credential checks
    additionally need the `terraform` CLI on PATH with access to infra/environments/prod's state
    (they run `terraform output -raw picture_service_access_key_id/secret_access_key` there — see
    infra/README.md's "Setting PictureService's AWS credentials as a Fly secret") and degrade to a
    WARN, not a failure, when that's unavailable. Every `aws` call is made with `--profile
    $AwsProfile` explicitly, so this script never depends on a `[default]` profile or an
    `AWS_PROFILE` env var being set in the caller's shell - except the scoped-credential probes,
    which intentionally swap in PictureService's own static keys via env vars for that one call
    (env-var credentials take precedence over `--profile` in the AWS CLI's resolution order, so the
    scoped probes still hit AWS as PictureService's IAM user, not as $AwsProfile).

.PARAMETER ProjectName
    Matches infra's `project_name` variable. Default: ninjago-scanner.

.PARAMETER AwsRegion
    Matches infra's `aws_region` variable. Default: eu-central-1.

.PARAMETER AwsProfile
    Named profile in ~/.aws/credentials and ~/.aws/config used for every operator-level `aws` call
    (sts, dynamodb, s3api). Default: terraform.

.PARAMETER WebUrl
    NinjagoScanner.Web's public URL. Default: https://ninjago-scanner-web.fly.dev.

.PARAMETER InfraDir
    Path to the Terraform root the scoped credentials are read from. Default:
    infra/environments/prod, relative to this script.

.EXAMPLE
    ./health-check.ps1
    ./health-check.ps1 -ProjectName ninjago-scanner -AwsRegion eu-central-1
#>

param(
    [string]$ProjectName = "ninjago-scanner",
    [string]$AwsRegion = "eu-central-1",
    [string]$AwsProfile = "terraform",
    [string]$WebUrl = "https://ninjago-scanner-web.fly.dev",
    [string]$InfraDir = (Join-Path $PSScriptRoot "..\environments\prod")
)

$ErrorActionPreference = "Stop"

# ---- helpers -----------------------------------------------------------

$script:Results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Component,
        [ValidateSet("OK", "WARN", "FAIL")][string]$Status,
        [string]$Detail,
        [Parameter(Mandatory)][ValidateSet("AWS", "Fly")][string]$Section
    )
    $script:Results.Add([pscustomobject]@{
        Section   = $Section
        Component = $Component
        Status    = $Status
        Detail    = $Detail
    })
}

function Invoke-AwsJson {
    param([string[]]$AwsArgs)

    $output = & aws @AwsArgs --profile $AwsProfile --region $AwsRegion --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join "`n")
    }
    if ([string]::IsNullOrWhiteSpace($output)) {
        return $null
    }
    return $output | ConvertFrom-Json
}

function Test-HttpEndpoint {
    param([string]$Url, [int]$TimeoutSec = 15)

    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec -Method Get
        return [pscustomobject]@{ Ok = $true; StatusCode = $response.StatusCode; Content = $response.Content; Error = $null }
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        $code = if ($resp) { [int]$resp.StatusCode } else { $null }
        return [pscustomobject]@{ Ok = $false; StatusCode = $code; Content = $null; Error = $_.Exception.Message }
    } catch {
        return [pscustomobject]@{ Ok = $false; StatusCode = $null; Content = $null; Error = $_.Exception.Message }
    }
}

# ---- preflight ----------------------------------------------------------

$hasAws = [bool](Get-Command aws -ErrorAction SilentlyContinue)
$hasFlyctl = [bool](Get-Command flyctl -ErrorAction SilentlyContinue)

if (-not $hasAws) {
    Add-Result "AWS CLI" "WARN" "'aws' not found on PATH - skipping DynamoDB/S3 checks." -Section AWS
}
if (-not $hasFlyctl) {
    Add-Result "flyctl CLI" "WARN" "'flyctl' not found on PATH - skipping Fly app checks." -Section Fly
}

$accountId = $null
if ($hasAws) {
    try {
        $identity = Invoke-AwsJson @("sts", "get-caller-identity")
        $accountId = $identity.Account
        Add-Result "AWS credentials" "OK" "Profile '$AwsProfile': account $accountId, ARN $($identity.Arn)" -Section AWS
    } catch {
        Add-Result "AWS credentials" "FAIL" "Could not call sts:get-caller-identity with profile '$AwsProfile' - $($_.Exception.Message)" -Section AWS
    }
}

if ($accountId) {

    # ---- DynamoDB sidecar tables ---------------------------------------------
    # See infra/modules/collection-sidecar-table/main.tf: this is the live table (CollectionId +
    # PhotoId key) PictureService actually talks to today. modules/sidecar-table's table (PhotoId
    # only) is the pre-migration one, kept only until a manual decommission - checked here just to
    # confirm it's still there and untouched, not as a sign anything is wrong if it eventually
    # disappears (that's an intentional future cleanup step, not a regression).

    $liveTableName = "$ProjectName-collection-sidecars"
    try {
        $table = Invoke-AwsJson @("dynamodb", "describe-table", "--table-name", $liveTableName)
        $status = $table.Table.TableStatus
        if ($status -eq "ACTIVE") {
            Add-Result "DynamoDB table (live)" "OK" "$liveTableName is ACTIVE ($($table.Table.ItemCount) items)" -Section AWS
        } else {
            Add-Result "DynamoDB table (live)" "FAIL" "$liveTableName status: $status" -Section AWS
        }
    } catch {
        Add-Result "DynamoDB table (live)" "FAIL" $_.Exception.Message -Section AWS
    }

    $legacyTableName = "$ProjectName-sidecars"
    try {
        $legacyTable = Invoke-AwsJson @("dynamodb", "describe-table", "--table-name", $legacyTableName)
        Add-Result "DynamoDB table (legacy)" "OK" "$legacyTableName still present ($($legacyTable.Table.ItemCount) items) - decommission once the migration is fully verified" -Section AWS
    } catch {
        Add-Result "DynamoDB table (legacy)" "WARN" "$legacyTableName not found - fine if already decommissioned, otherwise investigate: $($_.Exception.Message)" -Section AWS
    }

    # ---- S3 buckets -----------------------------------------------------------

    foreach ($bucket in @(
        @{ Name = "Photos bucket"; Bucket = "$ProjectName-photos-$accountId" }
    )) {
        try {
            & aws s3api head-bucket --bucket $bucket.Bucket --profile $AwsProfile --region $AwsRegion 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Add-Result $bucket.Name "OK" "$($bucket.Bucket) reachable" -Section AWS
            } else {
                Add-Result $bucket.Name "FAIL" "$($bucket.Bucket) not reachable" -Section AWS
            }
        } catch {
            Add-Result $bucket.Name "FAIL" $_.Exception.Message -Section AWS
        }
    }
}

# ---- PictureService's scoped IAM user: real read probes -------------------
# describe-table/head-bucket above only prove the resources exist to the operator's own (usually
# much broader) credentials. This section signs in as PictureService's actual IAM user
# (modules/iam-user) and performs the same kind of read the app's code does, within exactly the
# permissions that user has - GetItem/PutItem/DeleteItem/Scan/Query on the live sidecar table,
# GetObject/PutObject/DeleteObject/ListBucket scoped to the photos/* prefix. This is the check that
# catches a tightened bucket/table policy quietly breaking the app even though the resources
# themselves still exist and the operator's own credentials can still see them.

$hasTerraform = [bool](Get-Command terraform -ErrorAction SilentlyContinue)
$scopedAccessKeyId = $null
$scopedSecretAccessKey = $null

if (-not $hasTerraform) {
    Add-Result "PictureService scoped creds" "WARN" "'terraform' not found on PATH - skipping scoped S3/DynamoDB probes." -Section AWS
} elseif (-not (Test-Path $InfraDir)) {
    Add-Result "PictureService scoped creds" "WARN" "InfraDir '$InfraDir' not found - skipping scoped S3/DynamoDB probes." -Section AWS
} else {
    Push-Location $InfraDir
    try {
        $scopedAccessKeyId = (& terraform output -raw picture_service_access_key_id 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($scopedAccessKeyId -join "`n") }
        $scopedSecretAccessKey = (& terraform output -raw picture_service_secret_access_key 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($scopedSecretAccessKey -join "`n") }
        Add-Result "PictureService scoped creds" "OK" "Fetched via terraform output ($InfraDir)" -Section AWS
    } catch {
        Add-Result "PictureService scoped creds" "WARN" "Could not read terraform outputs - skipping scoped S3/DynamoDB probes: $($_.Exception.Message)" -Section AWS
        $scopedAccessKeyId = $null
        $scopedSecretAccessKey = $null
    } finally {
        Pop-Location
    }
}

if ($hasAws -and $accountId -and $scopedAccessKeyId -and $scopedSecretAccessKey) {
    $prevAccessKeyId = $env:AWS_ACCESS_KEY_ID
    $prevSecretAccessKey = $env:AWS_SECRET_ACCESS_KEY
    $prevSessionToken = $env:AWS_SESSION_TOKEN
    try {
        $env:AWS_ACCESS_KEY_ID = $scopedAccessKeyId
        $env:AWS_SECRET_ACCESS_KEY = $scopedSecretAccessKey
        Remove-Item Env:\AWS_SESSION_TOKEN -ErrorAction SilentlyContinue

        # Scan with a small limit is within the granted actions (GetItem/PutItem/DeleteItem/Scan/
        # Query) and needs no knowledge of an existing item's key, unlike GetItem.
        try {
            $scan = Invoke-AwsJson @("dynamodb", "scan", "--table-name", $liveTableName, "--limit", "1")
            Add-Result "Scoped: DynamoDB read" "OK" "$($liveTableName): scoped IAM user can Scan ($($scan.Count) item(s) returned)" -Section AWS
        } catch {
            Add-Result "Scoped: DynamoDB read" "FAIL" $_.Exception.Message -Section AWS
        }

        # list-objects-v2 with prefix "photos/" matches the ListBucket condition
        # (s3:prefix StringLike "photos/*") exactly - anything else would 403 even though the
        # bucket itself is reachable.
        try {
            $photosBucket = "$ProjectName-photos-$accountId"
            $listing = Invoke-AwsJson @("s3api", "list-objects-v2", "--bucket", $photosBucket, "--prefix", "photos/", "--max-keys", "1")
            Add-Result "Scoped: S3 read" "OK" "$($photosBucket): scoped IAM user can list photos/* ($($listing.KeyCount) key(s) returned)" -Section AWS
        } catch {
            Add-Result "Scoped: S3 read" "FAIL" $_.Exception.Message -Section AWS
        }
    } finally {
        $env:AWS_ACCESS_KEY_ID = $prevAccessKeyId
        $env:AWS_SECRET_ACCESS_KEY = $prevSecretAccessKey
        if ($prevSessionToken) { $env:AWS_SESSION_TOKEN = $prevSessionToken }
        $scopedAccessKeyId = $null
        $scopedSecretAccessKey = $null
    }
}

# ---- Fly apps ---------------------------------------------------------------

if ($hasFlyctl) {
    $flyApps = @(
        @{ Name = "Web";            App = "$ProjectName-web" },
        @{ Name = "CatalogService"; App = "$ProjectName-catalog-service" },
        @{ Name = "PictureService"; App = "$ProjectName-picture-service" }
    )

    foreach ($flyApp in $flyApps) {
        try {
            $statusJson = & flyctl status -a $flyApp.App --json 2>&1
            if ($LASTEXITCODE -ne 0) {
                Add-Result "Fly: $($flyApp.Name)" "FAIL" ($statusJson -join "`n") -Section Fly
                continue
            }
            $status = $statusJson | ConvertFrom-Json
            # A standby machine (Fly's pretty-printed table marks it with a dagger - "takes over
            # only in case of host hardware failure") is expected to be stopped; only a started
            # machine's checks count toward this app's health.
            $started = @($status.Machines | Where-Object { $_.state -eq "started" })
            $allChecksPassing = $true
            $checkDetails = @()
            foreach ($machine in $started) {
                foreach ($check in ($machine.checks | Where-Object { $_ })) {
                    $checkDetails += "$($check.name)=$($check.status)"
                    if ($check.status -ne "passing") {
                        $allChecksPassing = $false
                    }
                }
            }

            if ($started.Count -eq 0) {
                Add-Result "Fly: $($flyApp.Name)" "FAIL" "No machine in 'started' state ($($flyApp.App))" -Section Fly
            } elseif (-not $allChecksPassing) {
                Add-Result "Fly: $($flyApp.Name)" "FAIL" "Started, but a health check isn't passing: $($checkDetails -join ', ')" -Section Fly
            } elseif ($checkDetails.Count -gt 0) {
                Add-Result "Fly: $($flyApp.Name)" "OK" "$($started.Count) machine(s) started, checks: $($checkDetails -join ', ')" -Section Fly
            } else {
                # Web has no [checks] block in fly.toml - "started" plus the HTTP check below is
                # the real signal for it.
                Add-Result "Fly: $($flyApp.Name)" "OK" "$($started.Count) machine(s) started (no configured health check)" -Section Fly
            }
        } catch {
            Add-Result "Fly: $($flyApp.Name)" "FAIL" $_.Exception.Message -Section Fly
        }
    }
}

# ---- End-to-end HTTP check against Web's public login page -----------------
# The only unauthenticated, publicly reachable page - see web-user-authentication's "Route
# authorization" requirement (everything else redirects to login for an unauthenticated request).
# CatalogService/PictureService have no public Fly IP, so there is no external HTTP endpoint on
# them to check directly - their liveness is covered by the Fly machine/health-check section above.

$loginCheck = Test-HttpEndpoint -Url "$WebUrl/Account/Login"
if ($loginCheck.Ok -and $loginCheck.StatusCode -eq 200) {
    Add-Result "HTTP: $WebUrl/Account/Login" "OK" "200 OK" -Section Fly
} else {
    Add-Result "HTTP: $WebUrl/Account/Login" "FAIL" "$($loginCheck.StatusCode) $($loginCheck.Error)" -Section Fly
}

# ---- report ---------------------------------------------------------------

Write-Host ""
Write-Host "=== NinjagoScanner health check ($ProjectName / $AwsRegion) ===" -ForegroundColor Cyan
Write-Host ("Run at {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date))

$nameWidth = ($script:Results | ForEach-Object { $_.Component.Length } | Measure-Object -Maximum).Maximum
$sections = @(
    @{ Key = "AWS"; Title = "AWS (S3 / DynamoDB)" },
    @{ Key = "Fly"; Title = "Fly.io (services + end-to-end)" }
)
foreach ($section in $sections) {
    $sectionResults = @($script:Results | Where-Object { $_.Section -eq $section.Key })
    if ($sectionResults.Count -eq 0) { continue }

    Write-Host ""
    Write-Host "--- $($section.Title) ---" -ForegroundColor Cyan
    foreach ($r in $sectionResults) {
        $color = switch ($r.Status) {
            "OK"   { "Green" }
            "WARN" { "Yellow" }
            "FAIL" { "Red" }
        }
        $paddedName = $r.Component.PadRight($nameWidth)
        $paddedStatus = "[$($r.Status)]".PadRight(6)
        Write-Host "$paddedName  " -NoNewline
        Write-Host $paddedStatus -ForegroundColor $color -NoNewline
        Write-Host "  $($r.Detail)"
    }
}

Write-Host ""
$failCount = @($script:Results | Where-Object { $_.Status -eq "FAIL" }).Count
$warnCount = @($script:Results | Where-Object { $_.Status -eq "WARN" }).Count

if ($failCount -eq 0 -and $warnCount -eq 0) {
    Write-Host "All $($script:Results.Count) checks passed." -ForegroundColor Green
    exit 0
} elseif ($failCount -eq 0) {
    Write-Host "$warnCount warning(s), no failures." -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "$failCount failure(s), $warnCount warning(s) out of $($script:Results.Count) checks." -ForegroundColor Red
    exit 1
}
