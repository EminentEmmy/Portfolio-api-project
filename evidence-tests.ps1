<#
.SYNOPSIS
    Evidence scripts for the acceptance scenarios that plain .http requests cannot do.

.DESCRIPTION
    -Test Paging        Scenario 15: 25 transactions, page=2&pageSize=10 -> 10 items, totalCount 25, newest first
    -Test Concurrency   Scenario 16: simultaneous sells of the same holding -> one 201, the rest 409
    -Test Stopped       Scenario 18: database unavailable -> 500 ProblemDetails, no stack trace

    Each run prints PASS / FAIL lines and saves the whole output to the evidence folder:
        Paging       -> paging-test.txt
        Concurrency  -> concurrency-test.txt
        Stopped      -> 500-response.txt

    The API must already be running (dotnet run). The tests create their own portfolios
    (names include a timestamp), so they can be re-run. They add extra data, so run them
    AFTER you have saved the worked-example run, or on a database you are happy to reset.

.EXAMPLE
    .\evidence-tests.ps1 -Test Paging
    .\evidence-tests.ps1 -Test Concurrency
    .\evidence-tests.ps1 -Test Concurrency -Requests 10
    .\evidence-tests.ps1 -Test Stopped
    .\evidence-tests.ps1 -Test Paging -BaseUrl http://localhost:5220
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Paging', 'Concurrency', 'Stopped')]
    [string]$Test,

    [string]$BaseUrl = 'http://localhost:5220',

    # Concurrency test only: how many sells to fire at the same time.
    [ValidateRange(2, 50)]
    [int]$Requests = 2,

    [string]$EvidenceDir = (Join-Path $PSScriptRoot 'evidence')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Net.Http
# Windows PowerShell (.NET Framework) allows only 2 connections per host by default.
[System.Net.ServicePointManager]::DefaultConnectionLimit = 50

$BaseUrl = $BaseUrl.TrimEnd('/')
$script:Client = [System.Net.Http.HttpClient]::new()
$script:Client.Timeout = [TimeSpan]::FromSeconds(60)
$script:Failures = 0
$script:Log = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------

function Say([string]$Text = '') {
    Write-Host $Text
    $script:Log.Add($Text)
}

function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) {
        Say "PASS  $Name"
    }
    else {
        $script:Failures++
        Say "FAIL  $Name   $Detail"
    }
}

function New-ApiRequest([string]$Method, [string]$Path, $Body = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Compress -Depth 5
        $request.Content = [System.Net.Http.StringContent]::new(
            $json, [System.Text.Encoding]::UTF8, 'application/json')
    }
    return $request
}

# Reads a finished request. HttpClient returns 4xx and 5xx normally, so we always get the body.
function Read-ApiResult($Task) {
    $response = $Task.GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    $json = $null
    if ($text) {
        try { $json = $text | ConvertFrom-Json }
        catch { $json = $null }   # not JSON: keep the raw text in Body
    }

    return [pscustomobject]@{
        Status = [int]$response.StatusCode
        Body   = $text
        Json   = $json
    }
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
    $request = New-ApiRequest $Method $Path $Body
    return Read-ApiResult ($script:Client.SendAsync($request))
}

function Initialize-Asset {
    $r = Invoke-Api 'GET' '/api/assets/ACME'
    if ($r.Status -eq 200) { return }

    $r = Invoke-Api 'POST' '/api/assets' @{
        symbol       = 'ACME'
        name         = 'Acme Corp'
        assetType    = 'Stock'
        currentPrice = 50.00
    }
    if ($r.Status -ne 201) { throw "Could not create asset ACME: HTTP $($r.Status) $($r.Body)" }
}

function New-TestPortfolio([string]$Prefix) {
    $name = "$Prefix $(Get-Date -Format 'yyyyMMddHHmmss')"
    $r = Invoke-Api 'POST' '/api/portfolios' @{ name = $name; ownerEmail = 'ada@example.com' }
    if ($r.Status -ne 201) { throw "Could not create portfolio: HTTP $($r.Status) $($r.Body)" }
    return [int]$r.Json.id
}

function Add-Transaction([int]$PortfolioId, [string]$Type, [decimal]$Quantity, [decimal]$UnitPrice) {
    return Invoke-Api 'POST' "/api/portfolios/$PortfolioId/transactions" @{
        symbol    = 'ACME'
        type      = $Type
        quantity  = $Quantity
        unitPrice = $UnitPrice
        fee       = 0
    }
}

# ---------------------------------------------------------------
# Scenario 15: paging
# ---------------------------------------------------------------

function Test-Paging {
    Say '=== Scenario 15: 25 transactions, request page=2&pageSize=10 ==='
    Say 'Expected: 10 items, totalCount 25, newest first.'
    Say ''

    Initialize-Asset
    $portfolioId = New-TestPortfolio 'Paging'
    Say "Created portfolio $portfolioId. Adding 25 buys of ACME (1 share @ 50.00)..."

    $ids = @()
    foreach ($i in 1..25) {
        $r = Add-Transaction $portfolioId 'Buy' 1 50
        if ($r.Status -ne 201) { throw "Buy $i failed: HTTP $($r.Status) $($r.Body)" }
        $ids += [int]$r.Json.id
    }
    Say "Created transaction ids $($ids[0]) to $($ids[-1])."
    Say ''

    # Newest first = highest id first. Page 2 of size 10 = the 11th to 20th newest.
    $newestFirst = @($ids | Sort-Object -Descending)
    $expectedPage2 = @($newestFirst[10..19])

    $path = "/api/portfolios/$portfolioId/transactions?page=2&pageSize=10"
    $r = Invoke-Api 'GET' $path
    Say "GET $path -> HTTP $($r.Status)"
    Say $r.Body
    Say ''

    $got = @($r.Json.items | ForEach-Object { $_.id })

    Check 'status is 200' ($r.Status -eq 200) "got $($r.Status)"
    Check 'page is 2' ($r.Json.page -eq 2)
    Check 'pageSize is 10' ($r.Json.pageSize -eq 10)
    Check 'totalCount is 25' ($r.Json.totalCount -eq 25) "got $($r.Json.totalCount)"
    Check 'items has 10 entries' ($got.Count -eq 10) "got $($got.Count)"
    Check 'newest first: page 2 holds the 11th to 20th newest transactions' `
        (($got -join ',') -eq ($expectedPage2 -join ',')) `
        "expected $($expectedPage2 -join ','), got $($got -join ',')"

    $page3 = Invoke-Api 'GET' "/api/portfolios/$portfolioId/transactions?page=3&pageSize=10"
    Check 'page 3 returns the remaining 5 items' `
        ($page3.Status -eq 200 -and @($page3.Json.items).Count -eq 5)

    $tooBig = Invoke-Api 'GET' "/api/portfolios/$portfolioId/transactions?pageSize=101"
    Check 'pageSize 101 is rejected with 400' ($tooBig.Status -eq 400) "got $($tooBig.Status)"

    $missing = Invoke-Api 'GET' '/api/portfolios/999999/transactions'
    Check 'unknown portfolio returns 404' ($missing.Status -eq 404) "got $($missing.Status)"
}

# ---------------------------------------------------------------
# Scenario 16: concurrent sells
# ---------------------------------------------------------------

function Test-Concurrency {
    Say "=== Scenario 16: $Requests simultaneous sells of the same holding ==="
    Say 'Expected: exactly one 201, every other request 409, and the holding sold only once.'
    Say ''

    Initialize-Asset
    $portfolioId = New-TestPortfolio 'Concurrency'

    $buy = Add-Transaction $portfolioId 'Buy' 100 50
    if ($buy.Status -ne 201) { throw "Setup buy failed: HTTP $($buy.Status) $($buy.Body)" }
    Say "Portfolio $portfolioId holds 100 ACME at average cost 50.00."
    Say "Firing $Requests sells of 100 ACME @ 60.00 at the same moment..."
    Say ''

    # Warm up the connection so the sells really start together.
    Invoke-Api 'GET' "/api/portfolios/$portfolioId" | Out-Null

    $sellPath = "/api/portfolios/$portfolioId/transactions"
    $sellBody = @{ symbol = 'ACME'; type = 'Sell'; quantity = 100; unitPrice = 60; fee = 0 }

    # Build every request first, then send them in a tight loop.
    $messages = @()
    for ($i = 1; $i -le $Requests; $i++) {
        $messages += New-ApiRequest 'POST' $sellPath $sellBody
    }
    $tasks = @(foreach ($message in $messages) { $script:Client.SendAsync($message) })
    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks)

    $results = @()
    foreach ($task in $tasks) { $results += Read-ApiResult $task }

    $n = 0
    foreach ($r in $results) {
        $n++
        Say "Request $n -> HTTP $($r.Status)   $($r.Body)"
    }
    Say ''

    $created = @($results | Where-Object { $_.Status -eq 201 })
    $conflicts = @($results | Where-Object { $_.Status -eq 409 })

    Check 'exactly one sell returned 201' ($created.Count -eq 1) "got $($created.Count)"
    Check "all other sells returned 409" ($conflicts.Count -eq ($Requests - 1)) "got $($conflicts.Count) conflicts"

    $history = Invoke-Api 'GET' "/api/portfolios/$portfolioId/transactions"
    Check 'history holds exactly 2 transactions (1 buy + 1 sell)' `
        ($history.Json.totalCount -eq 2) "got $($history.Json.totalCount)"

    $holdings = Invoke-Api 'GET' "/api/portfolios/$portfolioId/holdings"
    Check 'holding is fully sold (holdings list is empty)' `
        ($holdings.Status -eq 200 -and $holdings.Body.Trim() -eq '[]') "got: $($holdings.Body)"

    $summary = Invoke-Api 'GET' "/api/portfolios/$portfolioId/summary"
    Check 'realized P&L was counted once (1000.00)' `
        ($summary.Json.realizedPnl -eq 1000) "got $($summary.Json.realizedPnl)"
}

# ---------------------------------------------------------------
# Scenario 18: database unavailable
# ---------------------------------------------------------------

function Test-Stopped {
    Say '=== Scenario 18: database unavailable -> 500 ProblemDetails, no stack trace ==='
    Say ''
    Say 'LocalDB restarts itself when something connects, so stopping it does not work.'
    Say 'Take the database offline instead. In SSMS, open a NEW query window'
    Say 'connected to (localdb)\MSSQLLocalDB, choose the master database, and run:'
    Say ''
    Say '    ALTER DATABASE PortfolioDb SET OFFLINE WITH ROLLBACK IMMEDIATE;'
    Say ''
    Read-Host 'Press Enter after that statement has finished' | Out-Null

    $r = Invoke-Api 'GET' '/api/assets/ACME'
    Say "GET /api/assets/ACME -> HTTP $($r.Status)"
    Say $r.Body
    Say ''

    Check 'status is 500' ($r.Status -eq 500) "got $($r.Status)"
    Check 'title is "An unexpected error occurred."' `
        ($r.Json.title -eq 'An unexpected error occurred.') "got: $($r.Json.title)"

    $markers = @('StackTrace', 'Exception', '   at ', 'Microsoft.', 'System.', 'SqlClient', 'PortfolioApi.')
    $leaked = @($markers | Where-Object { $r.Body -like "*$_*" })
    Check 'body contains no stack trace or exception details' `
        ($leaked.Count -eq 0) "found: $($leaked -join ', ')"

    Say ''
    Say 'NEXT: copy the matching "fail:" log entry from the terminal running dotnet run'
    Say '      into evidence\500-log.txt (the brief asks for the 500 response AND its log entry).'
    Say ''
    Say 'Then bring the database back. In the same SSMS window run:'
    Say ''
    Say '    ALTER DATABASE PortfolioDb SET ONLINE;'
    Say ''
    Read-Host 'Press Enter after the database is online again' | Out-Null

    # The first request after an outage may fail on a stale pooled connection, so allow a few tries.
    $recovered = $false
    foreach ($attempt in 1..3) {
        $again = Invoke-Api 'GET' '/api/assets/ACME'
        if ($again.Status -eq 200) { $recovered = $true; break }
        Start-Sleep -Seconds 1
    }
    Check 'API works again after the database is back online' $recovered "last status $($again.Status)"
}

# ---------------------------------------------------------------
# Run
# ---------------------------------------------------------------

Say "Run at $(Get-Date -Format 'u')   API: $BaseUrl"
Say ''

try {
    switch ($Test) {
        'Paging' { Test-Paging }
        'Concurrency' { Test-Concurrency }
        'Stopped' { Test-Stopped }
    }
}
catch {
    $script:Failures++
    Say "ERROR  $($_.Exception.Message)"
}
finally {
    $script:Client.Dispose()
}

Say ''
if ($script:Failures -eq 0) { Say 'RESULT: all checks passed' }
else { Say "RESULT: $($script:Failures) check(s) failed" }

if (-not (Test-Path $EvidenceDir)) { New-Item -ItemType Directory -Path $EvidenceDir | Out-Null }

$fileNames = @{ Paging = 'paging-test.txt'; Concurrency = 'concurrency-test.txt'; Stopped = '500-response.txt' }
$file = Join-Path $EvidenceDir $fileNames[$Test]
$script:Log | Set-Content -Path $file -Encoding UTF8
Write-Host "Saved: $file"