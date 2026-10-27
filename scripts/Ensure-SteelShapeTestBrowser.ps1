$ErrorActionPreference='Stop'
# CI dependency only. The installer itself can use the user's normal browser fallback.
$keys=@('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7E60D-F906-447E-8C93-D2D278E35178}',
        'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7E60D-F906-447E-8C93-D2D278E35178}')
$available=$keys | Where-Object {Test-Path $_} | ForEach-Object {(Get-ItemProperty $_ -ErrorAction SilentlyContinue).pv} | Where-Object {$_ -and $_ -ne '0.0.0.0'}
if(!$available){
    $setup=Join-Path $env:RUNNER_TEMP 'MicrosoftEdgeWebview2Setup.exe'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $setup
    $signature=Get-AuthenticodeSignature $setup
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'WebView2 bootstrapper signature is not Microsoft'}
    $process=Start-Process $setup -ArgumentList '/silent /install' -Wait -PassThru
    if($process.ExitCode -ne 0){throw "WebView2 bootstrapper exit $($process.ExitCode)"}
}
$chrome=@("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") | Where-Object {Test-Path $_} | Select-Object -First 1
if(!$chrome){throw 'Chrome is required for the offline browser tests'}
"STEEL_TEST_CHROMIUM=$chrome" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
