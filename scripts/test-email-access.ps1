[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Email,
    [switch]$RequestNewCode
)

# Standalone Windows test. Only public project settings are embedded.
# Codes and session tokens stay in this process and are never printed or saved.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectUrl = 'https://qwgfdbvbdfpgbbntezil.supabase.co'
$headers = @{ apikey = 'sb_publishable_GgZ8inQyADwVp-QIHwyEWA_R8aoro6H' }
$session = $null
$code = $null
$secureCode = $null
$body = $null
$codePointer = [IntPtr]::Zero

function Test-Approval {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$projectUrl/functions/v1/authorize-tool" `
            -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 20
        $decision = $response.Content | ConvertFrom-Json
        if ($response.StatusCode -eq 200 -and $decision.allowed -is [bool] -and $decision.allowed -eq $true) {
            Write-Host 'ACCESS APPROVED: the server permits this account.' -ForegroundColor Green
        } else {
            Write-Host 'ACCESS BLOCKED: the server did not explicitly allow access.'
        }
    } catch {
        $status = 0
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        switch ($status) {
            403 { Write-Host 'ACCESS BLOCKED (403): approval is missing, disabled, expired, or email verification is required.' }
            401 { Write-Host 'SESSION REJECTED (401): rerun this test and sign in again.' }
            default { Write-Host "Access check failed (HTTP $status). Access remains blocked; 0 means no HTTP response." }
        }
    }
}

try {
    if ($RequestNewCode) {
        $body = @{ email = $Email; create_user = $true } | ConvertTo-Json -Compress
        try {
            $null = Invoke-RestMethod -Method Post -Uri "$projectUrl/auth/v1/otp" `
                -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 20
        } catch {
            throw 'Could not request a code. Check the address and SMTP settings, or wait before resending.'
        }
        Write-Host 'Code requested. Check your inbox and enter the newest code below.'
    }

    $secureCode = Read-Host "Enter the sign-in code for $Email (input is hidden)" -AsSecureString
    $codePointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCode)
    $code = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($codePointer)
    if ([string]::IsNullOrWhiteSpace($code)) { throw 'No code entered.' }
    $body = @{ email = $Email; token = $code.Trim(); type = 'email' } | ConvertTo-Json -Compress
    try {
        $session = Invoke-RestMethod -Method Post -Uri "$projectUrl/auth/v1/verify" `
            -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 20
    } catch {
        throw 'Code verification failed. If it expired or was already used, rerun with -RequestNewCode.'
    }
    $code = $null
    $body = $null
    if ([string]::IsNullOrWhiteSpace($session.access_token)) { throw 'No valid session returned.' }
    $headers.Authorization = "Bearer $($session.access_token)"
    Write-Host 'SIGN-IN VERIFIED. Your email is confirmed; tool approval is checked separately.' -ForegroundColor Green
    Test-Approval
    Write-Host 'Tell Codex the result above. Keep this window open while approval is configured.'
    while ((Read-Host 'Press Enter to recheck approval with the SAME session, or type q to sign out') -ne 'q') {
        Test-Approval
    }
} catch {
    # Only our fixed diagnostic text is printed; HTTP bodies and credentials are not.
    Write-Host ('Test stopped: ' + $_.Exception.Message) -ForegroundColor Red
} finally {
    if ($session -and $session.access_token) {
        $headers.Authorization = "Bearer $($session.access_token)"
        try {
            $null = Invoke-RestMethod -Method Post -Uri "$projectUrl/auth/v1/logout?scope=local" `
                -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 20
            Write-Host 'Test session signed out.'
        } catch { Write-Host 'Server sign-out could not be confirmed. No credentials were saved to disk.' }
    }
    if ($codePointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($codePointer) }
    if ($secureCode) { $secureCode.Dispose() }
    $headers.Clear()
    $code = $null
    $body = $null
    $session = $null
}
