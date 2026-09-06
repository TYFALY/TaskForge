param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$ApiKey = "taskforge-dev-key-12345",
    [int]$MaxRetries = 30,
    [int]$RetryIntervalMs = 1000
)

$script:TestsPassed = 0
$script:TestsFailed = 0

function Write-TestResult {
    param([string]$Test, [bool]$Passed, [string]$Details = "")
    if ($Passed) {
        Write-Host "[PASS] $Test" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "[FAIL] $Test" -ForegroundColor Red
        if ($Details) { Write-Host "       $Details" -ForegroundColor Yellow }
        $script:TestsFailed++
    }
}

function Write-TestHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Test-HealthEndpoint {
    Write-TestHeader "Phase 1: Health Check"
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl/health" -Method GET -TimeoutSec 5 -UseBasicParsing
        $statusCode = $response.StatusCode
        $body = $response.Content | ConvertFrom-Json
        Write-Host "  Status Code: $statusCode"
        Write-Host "  Response: $($body | ConvertTo-Json)"
        $passed = $statusCode -eq 200 -and $body.status -eq "healthy"
        Write-TestResult "Health endpoint returns 200 OK" $passed
        return $passed
    }
    catch {
        Write-TestResult "Health endpoint is reachable" $false $_.Exception.Message
        return $false
    }
}

function Test-EnqueueJob {
    Write-TestHeader "Phase 2: Enqueue Job"
    # Create proper JSON payload using PSCustomObject (which ConvertTo-Json handles correctly)
    $payloadObj = [PSCustomObject]@{
        test = "e2e-integration"
        timestamp = (Get-Date).ToString("o")
    }
    $jsonPayload = $payloadObj | ConvertTo-Json -Compress
    Write-Host "  Payload: $jsonPayload"
    
    $body = @{
        queueName = "default"
        payload = $jsonPayload
        maxRetries = 3
        namespace = "default"
    }
    try {
        $headers = @{
            "X-TaskForge-Key" = $ApiKey
            "Content-Type" = "application/json"
        }
        $response = Invoke-WebRequest -Uri "$BaseUrl/api/v1/jobs/enqueue" -Method POST -Body ($body | ConvertTo-Json) -Headers $headers -TimeoutSec 10 -UseBasicParsing
        $statusCode = $response.StatusCode
        $result = $response.Content | ConvertFrom-Json
        Write-Host "  Status Code: $statusCode"
        Write-Host "  JobId: $($result.jobId)"
        Write-Host "  Queue: $($result.queueName)"
        Write-Host "  Status: $($result.status)"
        $passed = $statusCode -eq 202 -and $result.jobId -ne $null
        Write-TestResult "Job enqueued successfully (202 Accepted)" $passed
        Write-TestResult "JobId is returned" ($result.jobId -ne $null)
        return $result.jobId
    }
    catch {
        Write-TestResult "Job enqueue request" $false $_.Exception.Message
        return $null
    }
}

function Test-WebhookEnqueue {
    Write-TestHeader "Phase 2b: Enqueue Webhook Job (JSON Body Test)"
    # Body as JSON string (properly formatted)
    $bodyJson = @{
        message = "Hello from TaskForge E2E"
        data = @{ nested = "value" }
    } | ConvertTo-Json -Depth 3 -Compress
    Write-Host "  Body JSON: $bodyJson"
    
    $body = @{
        queueName = "webhooks"
        targetUrl = "https://httpbin.org/post"
        method = "POST"
        headers = @{ "X-Test-Header" = "taskforge-e2e" }
        body = $bodyJson
        timeoutSeconds = 30
        maxRetries = 2
    }
    try {
        $headers = @{
            "X-TaskForge-Key" = $ApiKey
            "Content-Type" = "application/json"
        }
        $response = Invoke-WebRequest -Uri "$BaseUrl/api/v1/jobs/webhook" -Method POST -Body ($body | ConvertTo-Json -Depth 5) -Headers $headers -TimeoutSec 10 -UseBasicParsing
        $statusCode = $response.StatusCode
        $result = $response.Content | ConvertFrom-Json
        Write-Host "  Status Code: $statusCode"
        Write-Host "  JobId: $($result.jobId)"
        Write-Host "  JobType: $($result.jobType) (2=Webhook)"
        # JobType is an enum: 0=Default, 1=Scheduled, 2=Webhook
        $passed = $statusCode -eq 202 -and $result.jobId -ne $null -and ($result.jobType -eq 2 -or $result.jobType -eq "Webhook")
        Write-TestResult "Webhook job enqueued successfully (202 Accepted)" $passed
        Write-TestResult "Job type is Webhook (enum value 2)" ($result.jobType -eq 2 -or $result.jobType -eq "Webhook")
        return $result.jobId
    }
    catch {
        Write-TestResult "Webhook job enqueue request" $false $_.Exception.Message
        return $null
    }
}

function Test-PollJobStatus {
    param([string]$JobId)
    Write-TestHeader "Phase 3: Poll Job Status"
    $headers = @{ "X-TaskForge-Key" = $ApiKey }
    $finalStatus = $null
    $completed = $false
    Write-Host "  Polling every $RetryIntervalMs ms (max $MaxRetries attempts)..."
    for ($i = 1; $i -le $MaxRetries; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "$BaseUrl/api/v1/jobs/$JobId" -Method GET -Headers $headers -TimeoutSec 5 -UseBasicParsing
            $result = $response.Content | ConvertFrom-Json
            $currentStatus = $result.status
            Write-Host "  Attempt $i/$MaxRetries - Status: $currentStatus" -NoNewline
            if ($currentStatus -eq "Completed") {
                Write-Host " [COMPLETED!]" -ForegroundColor Green
                $finalStatus = "Completed"
                $completed = $true
                break
            } elseif ($currentStatus -eq "Failed") {
                Write-Host " [FAILED]" -ForegroundColor Red
                $finalStatus = "Failed"
                break
            } elseif ($currentStatus -eq "DeadLettered") {
                Write-Host " [DEAD]" -ForegroundColor DarkRed
                $finalStatus = "DeadLettered"
                break
            } else {
                Write-Host ""
            }
            Start-Sleep -Milliseconds $RetryIntervalMs
        }
        catch {
            Write-Host "  Attempt $i - Error: $($_.Exception.Message)" -ForegroundColor Yellow
            Start-Sleep -Milliseconds $RetryIntervalMs
        }
    }
    if (-not $completed -and -not $finalStatus) {
        Write-Host "  Timeout reached after $MaxRetries attempts" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-TestResult "Job completed successfully" $completed "Final status: $finalStatus"
    Write-TestResult "Job did not fail" ($finalStatus -ne "Failed") "Status was: $finalStatus"
    return $completed
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host " TaskForge E2E Integration Test Suite" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "Configuration:" -ForegroundColor White
Write-Host "  Base URL: $BaseUrl"
Write-Host "  API Key:  $ApiKey"
Write-Host "  Timeout:  $($MaxRetries * $RetryIntervalMs / 1000)s"

$healthPassed = Test-HealthEndpoint

$jobId = $null
if ($healthPassed) {
    $jobId = Test-EnqueueJob
    $webhookJobId = Test-WebhookEnqueue
}

$pollPassed = $false
if ($jobId -ne $null) {
    $pollPassed = Test-PollJobStatus -JobId $jobId
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " TEST SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tests Passed: $script:TestsPassed" -ForegroundColor Green
Write-Host "  Tests Failed: $script:TestsFailed" -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
Write-Host ""
if ($script:TestsFailed -eq 0) {
    Write-Host "  OVERALL RESULT: ALL TESTS PASSED" -ForegroundColor Green
    Write-Host ""
    exit 0
} else {
    Write-Host "  OVERALL RESULT: SOME TESTS FAILED" -ForegroundColor Red
    Write-Host ""
    exit 1
}