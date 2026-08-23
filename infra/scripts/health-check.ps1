<#
.SYNOPSIS
    Health-checks NinjagoScanner's AWS resources and reports a single pass/fail view.

.DESCRIPTION
    As of the aws-compute-teardown change, infra/ is storage-only (see infra/README.md) — there is
    no ECS/Fargate, internal NLB, BFF Lambda, or web-client S3 bucket to check anymore. This script
    checks what's still deployed (the DynamoDB sidecar table, the S3 photo bucket) plus a CloudFront
    distribution lookup and end-to-end HTTP checks against the app's public entry point. Those two
    are expected to FAIL/skip until compute exists again (see openspec/changes/fly-hosting-migration)
    — kept here so this script needs minimal changes once a public entry point exists again, whatever
    form it takes.

    Resource names are almost all deterministic (see infra/modules/*/main.tf) except the CloudFront
    distribution, which gets its AWS-assigned ID discovered at runtime.

    Requires the AWS CLI (`aws`) on PATH, configured with credentials that can read these services
    (the same account/region infra/environments/prod deploys into).

.PARAMETER ProjectName
    Matches infra's `project_name` variable. Default: ninjago-scanner.

.PARAMETER AwsRegion
    Matches infra's `aws_region` variable. Default: eu-central-1.

.EXAMPLE
    ./health-check.ps1
    ./health-check.ps1 -ProjectName ninjago-scanner -AwsRegion eu-central-1
#>

param(
    [string]$ProjectName = "ninjago-scanner",
    [string]$AwsRegion = "eu-central-1"
)

$ErrorActionPreference = "Stop"

# ---- helpers -----------------------------------------------------------

$script:Results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Component,
        [ValidateSet("OK", "WARN", "FAIL")][string]$Status,
        [string]$Detail
    )
    $script:Results.Add([pscustomobject]@{
        Component = $Component
        Status    = $Status
        Detail    = $Detail
    })
}

function Invoke-AwsJson {
    param([string[]]$AwsArgs)

    $output = & aws @AwsArgs --region $AwsRegion --output json 2>&1
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

if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    Write-Host "AWS CLI ('aws') not found on PATH. Install it and configure credentials first." -ForegroundColor Red
    exit 2
}

try {
    $identity = Invoke-AwsJson @("sts", "get-caller-identity")
    $accountId = $identity.Account
    Add-Result "AWS credentials" "OK" "Account $accountId, ARN $($identity.Arn)"
} catch {
    Add-Result "AWS credentials" "FAIL" "Could not call sts:get-caller-identity - $($_.Exception.Message)"
    # Nothing below can work without credentials; report and stop here.
    $accountId = $null
}

if ($accountId) {

    # ---- DynamoDB sidecar table ----------------------------------------------

    $tableName = "$ProjectName-sidecars"
    try {
        $table = Invoke-AwsJson @("dynamodb", "describe-table", "--table-name", $tableName)
        $status = $table.Table.TableStatus
        if ($status -eq "ACTIVE") {
            Add-Result "DynamoDB table" "OK" "$tableName is ACTIVE ($($table.Table.ItemCount) items)"
        } else {
            Add-Result "DynamoDB table" "FAIL" "$tableName status: $status"
        }
    } catch {
        Add-Result "DynamoDB table" "FAIL" $_.Exception.Message
    }

    # ---- S3 buckets -----------------------------------------------------------

    foreach ($bucket in @(
        @{ Name = "Photos bucket"; Bucket = "$ProjectName-photos-$accountId" }
    )) {
        try {
            & aws s3api head-bucket --bucket $bucket.Bucket --region $AwsRegion 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Add-Result $bucket.Name "OK" "$($bucket.Bucket) reachable"
            } else {
                Add-Result $bucket.Name "FAIL" "$($bucket.Bucket) not reachable"
            }
        } catch {
            Add-Result $bucket.Name "FAIL" $_.Exception.Message
        }
    }

    # ---- CloudFront distribution -----------------------------------------------

    $cloudFrontDomain = $null
    try {
        $distributions = Invoke-AwsJson @("cloudfront", "list-distributions")
        $dist = $distributions.DistributionList.Items | Where-Object { $_.Comment -like "$ProjectName*" } | Select-Object -First 1
        if ($dist) {
            $cloudFrontDomain = $dist.DomainName
            if ($dist.Enabled -and $dist.Status -eq "Deployed") {
                Add-Result "CloudFront distribution" "OK" "$($dist.DomainName) - Enabled, Deployed"
            } else {
                Add-Result "CloudFront distribution" "WARN" "$($dist.DomainName) - Enabled=$($dist.Enabled), Status=$($dist.Status)"
            }
        } else {
            Add-Result "CloudFront distribution" "FAIL" "No distribution found with comment matching '$ProjectName*'"
        }
    } catch {
        Add-Result "CloudFront distribution" "FAIL" $_.Exception.Message
    }

    # ---- End-to-end HTTP checks through the real public path -------------------

    if ($cloudFrontDomain) {
        $baseUrl = "https://$cloudFrontDomain"

        # BFF-only endpoint: no downstream gRPC call, just proves CloudFront -> API Gateway -> Lambda works.
        $limits = Test-HttpEndpoint -Url "$baseUrl/api/uploads/limits"
        if ($limits.Ok -and $limits.StatusCode -eq 200) {
            Add-Result "HTTP: /api/uploads/limits" "OK" "200 OK"
        } else {
            Add-Result "HTTP: /api/uploads/limits" "FAIL" "$($limits.StatusCode) $($limits.Error)"
        }

        # Full chain: CloudFront -> API Gateway -> Lambda -> internal NLB -> CatalogService.
        $series = Test-HttpEndpoint -Url "$baseUrl/api/series"
        if ($series.Ok -and $series.StatusCode -eq 200 -and $series.Content -and $series.Content.Trim() -ne "[]") {
            Add-Result "HTTP: /api/series (full chain)" "OK" "200 OK, non-empty response"
        } elseif ($series.Ok -and $series.StatusCode -eq 200) {
            Add-Result "HTTP: /api/series (full chain)" "WARN" "200 OK but empty response - check CatalogService data"
        } else {
            Add-Result "HTTP: /api/series (full chain)" "FAIL" "$($series.StatusCode) $($series.Error)"
        }
    } else {
        Add-Result "HTTP end-to-end checks" "WARN" "Skipped - no CloudFront domain discovered"
    }
}

# ---- report ---------------------------------------------------------------

Write-Host ""
Write-Host "=== NinjagoScanner health check ($ProjectName / $AwsRegion) ===" -ForegroundColor Cyan
Write-Host ("Run at {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date))
Write-Host ""

$nameWidth = ($script:Results | ForEach-Object { $_.Component.Length } | Measure-Object -Maximum).Maximum
foreach ($r in $script:Results) {
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

Write-Host ""
$failCount = ($script:Results | Where-Object { $_.Status -eq "FAIL" }).Count
$warnCount = ($script:Results | Where-Object { $_.Status -eq "WARN" }).Count

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
