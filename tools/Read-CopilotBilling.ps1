#Requires -Version 5.1
<#
.SYNOPSIS
Reads the current user's monthly GitHub AI Credits billing report.
.DESCRIPTION
Prompts for a classic PAT using hidden console input. Sends it only to
api.github.com, disables redirects, and never saves the token.
The diagnostic report omits the account name and does not calculate a balance.
.EXAMPLE
.\tools\Read-CopilotBilling.ps1 -Describe
.EXAMPLE
.\tools\Read-CopilotBilling.ps1
.EXAMPLE
.\tools\Read-CopilotBilling.ps1 -Year 2026 -Month 9
#>
[CmdletBinding()]
param(
    [ValidateRange(2026, 9999)]
    [int] $Year = [DateTime]::UtcNow.Year,

    [ValidateRange(1, 12)]
    [int] $Month = [DateTime]::UtcNow.Month,

    [string] $ReportDirectory = (Join-Path $env:LOCALAPPDATA 'CopilotUsage\Diagnostics'),

    [switch] $Describe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$apiVersion = '2026-03-10'
Write-Warning 'Legacy Billing diagnostic only. This is NOT Settings > Copilot > Features > Usage and is no longer used by the tray app.'

if ($Describe) {
    Write-Output "Period: $Year-$('{0:D2}' -f $Month) (UTC calendar month)"
    Write-Output 'Identity: GET https://api.github.com/user'
    Write-Output 'Usage: GET https://api.github.com/users/{authenticated-login}/settings/billing/ai_credit/usage'
    Write-Output "API version: $apiVersion"
    Write-Output 'Authentication: classic PAT, hidden interactive input; not saved.'
    Write-Output 'Requests: two read-only GETs, no automatic retries or redirects.'
    Write-Output 'Output: account-redacted diagnostic report; no balance inferred.'
    Write-Output "Report directory: $ReportDirectory"
    return
}

Add-Type -AssemblyName System.Net.Http

function Get-OptionalHeader {
    param($Headers, [string] $Name)

    if ($Headers.Contains($Name)) {
        return [string]::Join(', ', $Headers.GetValues($Name))
    }
    return $null
}

function Get-GitHubJson {
    param(
        [System.Net.Http.HttpClient] $Client,
        [string] $Path
    )

    $uri = [Uri]::new("https://api.github.com$Path")
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'api.github.com' -or $uri.Port -ne 443) {
        throw 'Refusing to send credentials outside the GitHub API.'
    }

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Get, $uri)
    $response = $null
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        $status = [int] $response.StatusCode
        $acceptedScopes = Get-OptionalHeader $response.Headers 'X-Accepted-OAuth-Scopes'

        if (-not $response.IsSuccessStatusCode) {
            $reason = switch ($status) {
                401 { 'The token was rejected or has expired.' }
                403 { 'Access was denied: check billing permissions or rate limits.' }
                404 { 'The report is unavailable: permissions, account billing ownership, or endpoint support may be the cause.' }
                429 { 'GitHub rate-limited the request. Do not retry immediately.' }
                default { 'GitHub did not return a successful report. No automatic retry was attempted.' }
            }
            $diagnostic = "GitHub HTTP ${status}: $reason"
            if ($acceptedScopes) {
                $diagnostic += " Accepted OAuth scopes reported by GitHub: $acceptedScopes."
            }
            $retryAfter = Get-OptionalHeader $response.Headers 'Retry-After'
            if ($retryAfter) {
                $diagnostic += " Retry-After: $retryAfter."
            }
            # Never include the raw error body or the request's authentication headers.
            throw [System.InvalidOperationException]::new($diagnostic)
        }

        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([string]::IsNullOrWhiteSpace($body)) {
            throw 'GitHub returned an empty response; this is not zero usage.'
        }
        try {
            $data = ConvertFrom-Json -InputObject $body -ErrorAction Stop
        }
        catch [System.ArgumentException] {
            throw 'GitHub returned invalid JSON; the response body was not logged.'
        }
        return [pscustomobject] @{
            Data = $data
            AcceptedScopes = $acceptedScopes
            FetchedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose()
    }
}

function ConvertTo-UsageDiagnostic {
    param(
        $Response,
        [int] $RequestedYear,
        [int] $RequestedMonth
    )

    $data = $Response.Data
    if ($null -eq $data -or $null -eq $data.PSObject.Properties['timePeriod'] -or
        $null -eq $data.PSObject.Properties['usageItems']) {
        throw 'Unexpected billing schema: timePeriod or usageItems is missing.'
    }
    $period = $data.timePeriod
    if ($null -eq $period -or $null -eq $period.PSObject.Properties['year'] -or
        $null -eq $period.PSObject.Properties['month'] -or
        $period.year -ne $RequestedYear -or $period.month -ne $RequestedMonth) {
        throw 'GitHub returned an unexpected reporting period; no usage was inferred.'
    }
    if ($null -eq $data.usageItems -or $data.usageItems -isnot [System.Array]) {
        throw 'Unexpected billing schema: usageItems must be an array, not null.'
    }

    $textFields = @('product', 'sku', 'model', 'unitType')
    $numberFields = @(
        'pricePerUnit', 'grossQuantity', 'grossAmount', 'discountQuantity',
        'discountAmount', 'netQuantity', 'netAmount'
    )
    $items = @(
        foreach ($item in $data.usageItems) {
            if ($null -eq $item) {
                throw 'Unexpected billing schema: a usage item is null.'
            }
            $safeItem = [ordered] @{}
            foreach ($field in $textFields) {
                if ($null -eq $item.PSObject.Properties[$field] -or
                    $item.$field -isnot [string]) {
                    throw "Unexpected billing schema: $field must be a string."
                }
                $safeItem[$field] = $item.$field
            }
            foreach ($field in $numberFields) {
                if ($null -eq $item.PSObject.Properties[$field]) {
                    throw "Unexpected billing schema: $field is missing."
                }
                $value = $item.$field
                if ($value -isnot [int] -and $value -isnot [long] -and
                    $value -isnot [decimal] -and $value -isnot [double]) {
                    throw "Unexpected billing schema: $field must be numeric."
                }
                if ($value -is [double] -and
                    ([double]::IsNaN($value) -or [double]::IsInfinity($value))) {
                    throw "Unexpected billing schema: $field must be finite."
                }
                $safeItem[$field] = $value
            }
            [pscustomobject] $safeItem
        }
    )

    return [ordered] @{
        schemaVersion = 1
        status = 'retrieved-not-yet-reconciled'
        source = 'GitHub personal AI Credits billing API'
        apiVersion = $apiVersion
        account = '[redacted]'
        timePeriod = @{ year = $RequestedYear; month = $RequestedMonth }
        fetchedAtUtc = $Response.FetchedAtUtc
        serverDataAsOf = $null
        acceptedOAuthScopes = $Response.AcceptedScopes
        usageItems = $items
        totalAllowance = $null
        remainingAllowance = $null
        notes = @(
            'Compare product, SKU, unit and amounts with the official usage page.'
            'No quantity or discount field has been assumed to equal allowance consumption.'
            'No total allowance, remaining balance or zero balance has been inferred.'
            'GitHub server-side aggregation latency is unknown.'
            'Numeric values are parsed by PowerShell; this is a diagnostic, not an accounting ledger.'
        )
    }
}

$handler = $null
$client = $null
$secret = $null
$tokenText = $null
try {
    Write-Host 'This probe reads your personal GitHub billing report. It does not change budgets.'
    Write-Host 'Do NOT paste a token into chat. Input below is hidden and is not saved.'
    Write-Host 'Use a classic PAT with only the permissions needed for your personal billing report.'
    $secret = Read-Host 'Classic PAT' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $tokenText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    if ([string]::IsNullOrWhiteSpace($tokenText) -or $tokenText -match '\s') {
        throw 'A non-empty token without whitespace is required.'
    }
    if ($tokenText.StartsWith('github_pat_', [StringComparison]::Ordinal)) {
        throw 'This is a fine-grained PAT. GitHub billing usage documentation requires a classic PAT.'
    }
    if ($tokenText -cnotmatch '^(ghp_[A-Za-z0-9]+|[A-Fa-f0-9]{40})$') {
        throw 'The input is not in a recognized classic PAT format.'
    }

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenText)
    $tokenText = $null
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('CopilotUsage-BillingProbe/0.1')
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
    $client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', $apiVersion)

    $identity = Get-GitHubJson $client '/user'
    if ($null -eq $identity.Data -or $null -eq $identity.Data.PSObject.Properties['login'] -or
        $identity.Data.login -isnot [string] -or
        [string]::IsNullOrWhiteSpace($identity.Data.login)) {
        throw 'GitHub did not return an authenticated account login.'
    }
    Write-Host "Authenticated account: $($identity.Data.login)"
    $login = [Uri]::EscapeDataString($identity.Data.login)
    $response = Get-GitHubJson $client "/users/$login/settings/billing/ai_credit/usage?year=$Year&month=$Month"
    $report = ConvertTo-UsageDiagnostic $response $Year $Month

    $directory = [IO.Path]::GetFullPath($ReportDirectory)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $fileName = 'billing-probe-{0}-{1}.json' -f (
        [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')), [Guid]::NewGuid().ToString('N')
    $reportPath = Join-Path $directory $fileName
    $json = ConvertTo-Json -InputObject $report -Depth 8
    $stream = [IO.File]::Open($reportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }

    Write-Host "Report saved: $reportPath"
    Write-Host "Usage rows: $($report.usageItems.Count)"
    Write-Host 'Compare these rows with https://github.com/settings/billing before interpreting a balance.'
    Write-Host 'A successful fetch does not prove that GitHub has included your latest interaction.'
    Write-Output $reportPath
}
catch [System.Net.Http.HttpRequestException] {
    throw 'Cannot connect securely to the GitHub API. Check your network, proxy and certificate settings; no usage was inferred.'
}
catch [System.OperationCanceledException] {
    throw 'The GitHub request timed out or was cancelled; no usage was inferred.'
}
finally {
    $tokenText = $null
    if ($null -ne $client) {
        $client.DefaultRequestHeaders.Authorization = $null
        $client.Dispose()
    }
    elseif ($null -ne $handler) {
        $handler.Dispose()
    }
    if ($null -ne $secret) { $secret.Dispose() }
}
