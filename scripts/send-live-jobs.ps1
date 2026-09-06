# TaskForge Live Job Sender - Real-time integration test
$uri = "http://localhost:5000/api/v1/jobs/enqueue"
$queues = @("emails", "reports", "notifications")

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " TaskForge Live Job Sender" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Target API: $uri" -ForegroundColor White
Write-Host "Jobs to send: 100" -ForegroundColor White
Write-Host "Delay: 100-500ms random" -ForegroundColor White
Write-Host ""

$success = 0
$failed = 0
$start = Get-Date

for ($i = 1; $i -le 100; $i++) {
    $queueIndex = $i % 3
    $queue = $queues[$queueIndex]
    
    # Payload must be a JSON string
    switch ($queue) {
        "emails" {
            $payloadObj = @{
                Type = "SendReceiptEmail"
                CustomerEmail = "user$i@example.com"
                OrderId = "ORD-$i"
            }
        }
        "reports" {
            $storeNum = ($i % 5) + 1
            $payloadObj = @{
                Type = "GeneratePdfInvoice"
                StoreId = "STORE-$storeNum"
                DateRange = "2026-09"
            }
        }
        "notifications" {
            $payloadObj = @{
                Type = "SendShippedSMS"
                Phone = "+1555010$i"
                TrackingNum = "TRK-$i"
            }
        }
    }
    
    # Convert payload to JSON string
    $payloadStr = $payloadObj | ConvertTo-Json -Compress

    $body = @{
        QueueName = $queue
        Payload = $payloadStr
    } | ConvertTo-Json -Compress

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
        $success++
        Write-Host "[$i/100] SUCCESS | Queue: $queue | Job ID: $($response.JobId)" -ForegroundColor Green
    } catch {
        $failed++
        $errMsg = $_.Exception.Message
        Write-Host "[$i/100] FAILED  | Queue: $queue | Error: $errMsg" -ForegroundColor Red
    }

    if ($i -lt 100) {
        $delay = Get-Random -Minimum 100 -Maximum 500
        Start-Sleep -Milliseconds $delay
    }
}

$elapsed = ((Get-Date) - $start).TotalSeconds
$rate = [math]::Round(100 / $elapsed, 2)

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Job Submission Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Total Sent    : 100" -ForegroundColor White
Write-Host "  Successful   : $success" -ForegroundColor $(if ($success -eq 100) { "Green" } else { "Yellow" })
Write-Host "  Failed       : $failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host ""
Write-Host "  Elapsed Time : $([math]::Round($elapsed, 2)) seconds" -ForegroundColor White
Write-Host "  Rate         : $rate jobs/sec" -ForegroundColor White
Write-Host ""
if ($failed -eq 0) {
    Write-Host "  All jobs submitted successfully!" -ForegroundColor Green
} else {
    Write-Host "  Some jobs failed. Check API connectivity." -ForegroundColor Yellow
}
Write-Host ""