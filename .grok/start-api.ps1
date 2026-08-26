$ErrorActionPreference = 'Stop'
Set-Location 'c:\Users\punko\Downloads\PoSeeReview'

# Stop anything bound to the API ports.
Get-NetTCPConnection -LocalPort 5000,5001 -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
Get-Process -Name 'PoSeeReview.Api' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = "c:\Users\punko\Downloads\PoSeeReview\.grok\api-$stamp.out.log"
$err = "c:\Users\punko\Downloads\PoSeeReview\.grok\api-$stamp.err.log"

$p = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run','--project','src/PoSeeReview.Api','--launch-profile','https') `
    -WorkingDirectory 'c:\Users\punko\Downloads\PoSeeReview' `
    -RedirectStandardOutput $out `
    -RedirectStandardError $err `
    -WindowStyle Hidden `
    -PassThru

"started pid=$($p.Id)" | Set-Content 'c:\Users\punko\Downloads\PoSeeReview\.grok\api.pid.txt'
"out=$out" | Add-Content 'c:\Users\punko\Downloads\PoSeeReview\.grok\api.pid.txt'
"err=$err" | Add-Content 'c:\Users\punko\Downloads\PoSeeReview\.grok\api.pid.txt'
Write-Output "started pid=$($p.Id)"
Write-Output "out=$out"
