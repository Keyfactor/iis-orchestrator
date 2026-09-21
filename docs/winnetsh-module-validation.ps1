<#
WinNetSH module — end-to-end validation script (public PowerShell functions, no Command needed).

Run this INTERACTIVELY, ELEVATED (local Administrator - binding/unbinding sslcert entries requires
it), on a disposable/test Windows Server, under Windows PowerShell 5.1 (powershell.exe, NOT pwsh) -
that's the runtime IISU/PSHelper.cs actually launches for local execution.

This script calls the actual public functions Management.cs/Inventory.cs invoke:
New-KeyfactorNetSHBinding, Get-KeyfactorNetSHBoundCertificates, Remove-KeyfactorNetSHBinding,
Remove-KeyfactorNetSHCertificateIfUnused - including the netsh-output parsing and AppId resolution
logic. This is the closest you can get to testing "the extension" without wiring it into Command.

Steps 0-5 below test the LOCAL-AGENT path (functions running in-process). See the "JEA VARIANT"
section after Step 5 for the equivalent remote-via-JEA test - see docs/winnetsh-implementation-notes.md,
"Remaining unverified assumptions" for what this is meant to confirm.

Prerequisites: run elevated directly on the test server. Picks an unused local test port (8443) so
it doesn't collide with anything already bound to 443.
#>

Import-Module "$PSScriptRoot\..\IISU\PowerShell\Keyfactor.WinCert.NetSH\Keyfactor.WinCert.NetSH.psm1" -Force

$testIPAddress = '0.0.0.0'
$testPort = '8443'
$testHostName = 'winnetsh-validation.example.com'   # used only in the SNI/hostnameport step

# ============================================================================
# STEP 0 - Create a disposable test certificate.
# ============================================================================
$testCert = New-SelfSignedCertificate `
    -Subject "CN=$($env:COMPUTERNAME)" `
    -DnsName @($env:COMPUTERNAME) `
    -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyExportPolicy Exportable

Write-Host "Created test certificate with thumbprint: $($testCert.Thumbprint)" -ForegroundColor Cyan

# ============================================================================
# STEP 1 - Add/bind (ipport). Exercises: no-existing-binding path -> AppId generation -> netsh add.
# ============================================================================
Write-Host "`n=== STEP 1: New-KeyfactorNetSHBinding (new ipport binding, no AppId supplied) ===" -ForegroundColor Yellow
$addResult = New-KeyfactorNetSHBinding `
    -IPAddress $testIPAddress `
    -Port $testPort `
    -Thumbprint $testCert.Thumbprint `
    -StoreName 'My'
$addResult | Format-List
if ($addResult.Status -ne 'Success') {
    Write-Host "Add failed - stop here and inspect the error above before continuing." -ForegroundColor Red
}
else {
    Write-Host "Resolved AppId ($($addResult.Details.AppIdSource)): $($addResult.Details.AppId)" -ForegroundColor Cyan
}

# ============================================================================
# STEP 2 - Inventory. Exercises: netsh show sslcert -> output parsing -> certstorename scoping ->
# certificate re-read from the store.
# ============================================================================
Write-Host "`n=== STEP 2: Get-KeyfactorNetSHBoundCertificates ===" -ForegroundColor Yellow
Get-KeyfactorNetSHBoundCertificates -StoreName 'My'

# Confirm netsh's own view matches what the module returned, in case the parser is wrong about the
# output format on this OS version:
Write-Host "`nRaw 'netsh http show sslcert' output for comparison:" -ForegroundColor Cyan
netsh http show sslcert "ipport=$($testIPAddress):$testPort"

# ============================================================================
# STEP 3 - Renewal Add. Exercises: existing-binding-found path -> AppId REUSE (not regenerated) ->
# delete-then-add.
# ============================================================================
Write-Host "`n=== STEP 3: New-KeyfactorNetSHBinding (renewal - same ipport, new certificate) ===" -ForegroundColor Yellow
$renewedCert = New-SelfSignedCertificate `
    -Subject "CN=$($env:COMPUTERNAME)" `
    -DnsName @($env:COMPUTERNAME) `
    -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyExportPolicy Exportable

$renewResult = New-KeyfactorNetSHBinding `
    -IPAddress $testIPAddress `
    -Port $testPort `
    -Thumbprint $renewedCert.Thumbprint `
    -StoreName 'My'
$renewResult | Format-List
Write-Host "Expect AppIdSource='Reused' and AppId equal to Step 1's AppId ($($addResult.Details.AppId)):" -ForegroundColor Cyan
Write-Host "  Got AppIdSource='$($renewResult.Details.AppIdSource)', AppId='$($renewResult.Details.AppId)'" -ForegroundColor Cyan

# ============================================================================
# STEP 4 - SNI/hostnameport binding. Exercises the -HostName path end to end.
# ============================================================================
Write-Host "`n=== STEP 4: New-KeyfactorNetSHBinding (hostnameport / SNI binding) ===" -ForegroundColor Yellow
$sniResult = New-KeyfactorNetSHBinding `
    -IPAddress $testIPAddress `
    -Port $testPort `
    -HostName $testHostName `
    -Thumbprint $testCert.Thumbprint `
    -StoreName 'My'
$sniResult | Format-List

# ============================================================================
# STEP 5 - Remove. Removes both the sslcert binding and (if unused elsewhere) the certificate.
# ============================================================================
Write-Host "`n=== STEP 5: Remove-KeyfactorNetSHBinding + Remove-KeyfactorNetSHCertificateIfUnused ===" -ForegroundColor Yellow
Remove-KeyfactorNetSHBinding -IPAddress $testIPAddress -Port $testPort | Format-List
Remove-KeyfactorNetSHBinding -IPAddress $testIPAddress -Port $testPort -HostName $testHostName | Format-List
Remove-KeyfactorNetSHCertificateIfUnused -Thumbprint $testCert.Thumbprint -StoreName 'My' | Format-List
Remove-KeyfactorNetSHCertificateIfUnused -Thumbprint $renewedCert.Thumbprint -StoreName 'My' | Format-List

# ============================================================================
# JEA VARIANT - repeat Steps 1-5 through a real JEA session instead of in-process, to test the
# actual open question: does the JEA run-as account (virtual account or gMSA) have sufficient rights
# to run netsh http add/delete sslcert? Run this from a SEPARATE machine, once you've registered a
# JEA endpoint on the target server per docsource/content.md, with Keyfactor.WinCert.NetSH installed
# alongside Keyfactor.WinCert.Common and added to the endpoint's RoleDefinitions.
# ============================================================================
<#
$cred = Get-Credential   # account in the JEA endpoint's RoleDefinitions
$jeaSession = New-PSSession -ComputerName '<target-server>' -ConfigurationName '<your-jea-endpoint-name>' -Credential $cred

# Confirm identity/group-membership/JEA health first.
Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorDiagnostics } -InformationAction Continue

$jeaTestCert = New-SelfSignedCertificate -Subject "CN=jea-validation" -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") -KeyExportPolicy Exportable

Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Thumbprint)
    New-KeyfactorNetSHBinding -IPAddress '0.0.0.0' -Port '8443' -Thumbprint $Thumbprint -StoreName 'My'
} -ArgumentList $jeaTestCert.Thumbprint

Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorNetSHBoundCertificates -StoreName 'My' }

Invoke-Command -Session $jeaSession -ScriptBlock { Remove-KeyfactorNetSHBinding -IPAddress '0.0.0.0' -Port '8443' }
Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Thumbprint)
    Remove-KeyfactorNetSHCertificateIfUnused -Thumbprint $Thumbprint -StoreName 'My'
} -ArgumentList $jeaTestCert.Thumbprint

Remove-Item "Cert:\LocalMachine\My\$($jeaTestCert.Thumbprint)" -Force -ErrorAction SilentlyContinue
Remove-PSSession $jeaSession
#>

# ============================================================================
# CLEANUP - safety-net only. Step 5 already removes both test certificates via
# Remove-KeyfactorNetSHCertificateIfUnused, so this should normally find nothing left to do.
# ============================================================================
function Remove-WinNetSHModuleTestArtifacts {
    param($Thumbprint)
    Remove-Item "Cert:\LocalMachine\My\$Thumbprint" -Force -ErrorAction SilentlyContinue
    Write-Host "Removed '$Thumbprint' from Cert:\LocalMachine\My (if it was still there)." -ForegroundColor Green
}

Write-Host "`nIf anything above was left in place, run:" -ForegroundColor Green
Write-Host "  Remove-WinNetSHModuleTestArtifacts -Thumbprint '$($testCert.Thumbprint)'" -ForegroundColor Green
Write-Host "  Remove-WinNetSHModuleTestArtifacts -Thumbprint '$($renewedCert.Thumbprint)'" -ForegroundColor Green
Write-Host "  netsh http delete sslcert ipport=$($testIPAddress):$testPort" -ForegroundColor Green
Write-Host "  netsh http delete sslcert hostnameport=$($testHostName):$testPort" -ForegroundColor Green
