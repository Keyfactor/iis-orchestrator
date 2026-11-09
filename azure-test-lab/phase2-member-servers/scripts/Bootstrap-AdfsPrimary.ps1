#Requires -RunAsAdministrator
<#
    Runs on adfs-01 as Local System (the default for every bootstrap
    script in this lab). Installs the AD FS role, generates a self-signed
    bootstrap certificate for the federation service, and creates the
    farm. This bootstrap certificate is deliberately the same
    "Service Communications" certificate the WinAdfs store type is meant to
    rotate - so the first WinAdfs test you run against this lab is a
    natural, meaningful one.

    Install-AdfsFarm needs to run as a domain admin (to create the ADFS
    DKM container in AD), but Local System on a member server - which is
    just this machine's own computer account - isn't one. The
    Microsoft.Compute runCommand resource type has its own runAsUser/
    runAsPassword feature for exactly this kind of case, but it failed in
    testing with a Win32 "Incorrect function" error pointing at the
    Secondary Logon service path it depends on. Rather than fight that,
    this script creates a short-lived scheduled task that runs just the
    Install-AdfsFarm step as the domain admin - Task Scheduler's own
    impersonation path doesn't go through Secondary Logon, and it already
    auto-grants "Log on as a batch job" to whatever account /RU names.

    Idempotent: if the ADFS service is already running and farm
    information is already available, this script exits without changing
    anything.

    The certificate (with its private key) is exported to a share on dc-01
    so Bootstrap-AdfsSecondary.ps1 can import the *same* certificate onto
    adfs-02 - Add-AdfsFarmNode requires every node to present an identical
    certificate for the federation endpoint.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $DomainName,

    [Parameter(Mandatory = $true)]
    [string] $FederationServiceName,

    [Parameter(Mandatory = $true)]
    [string] $NetbiosName,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountName,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountPassword,

    [Parameter(Mandatory = $true)]
    [string] $DcHostName,

    [Parameter(Mandatory = $true)]
    [string] $CertExportPassword,

    [Parameter(Mandatory = $true)]
    [string] $DomainAdminUsername,

    [Parameter(Mandatory = $true)]
    [string] $DomainAdminPassword
)

$ErrorActionPreference = 'Stop'

Write-Output "Installing AD FS role..."
Install-WindowsFeature -Name ADFS-Federation -IncludeManagementTools | Out-Null

# Give the service account local admin rights on this node - simplest way
# to guarantee it can read the certificate's private key and start the
# ADFS service; matches the "simple domain account, not a gMSA" choice
# made for this lab.
$adminGroupMember = Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "*\$AdfsServiceAccountName" }
if (-not $adminGroupMember) {
    Add-LocalGroupMember -Group 'Administrators' -Member "$NetbiosName\$AdfsServiceAccountName"
}

$farmAlreadyExists = $false
try {
    $service = Get-Service -Name adfssrv -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Get-AdfsFarmInformation -ErrorAction Stop | Out-Null
        $farmAlreadyExists = $true
    }
}
catch {
    $farmAlreadyExists = $false
}

if ($farmAlreadyExists) {
    Write-Output "ADFS farm already exists on this node - skipping farm creation."
    exit 0
}

Write-Output "Generating self-signed bootstrap certificate for $FederationServiceName..."
$cert = New-SelfSignedCertificate `
    -DnsName $FederationServiceName `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -KeyExportPolicy Exportable `
    -KeySpec KeyExchange `
    -NotAfter (Get-Date).AddYears(2)

Write-Output "Bootstrap certificate thumbprint: $($cert.Thumbprint)"

# --- Run Install-AdfsFarm as a domain admin via a short-lived scheduled task ---
$workDir = 'C:\LabBootstrap'
New-Item -Path $workDir -ItemType Directory -Force | Out-Null
$innerScriptPath = Join-Path $workDir 'install-adfs-farm.ps1'
$innerLogPath = Join-Path $workDir 'install-adfs-farm.log'
$innerDonePath = Join-Path $workDir 'install-adfs-farm.done'
Remove-Item $innerDonePath, $innerLogPath -ErrorAction SilentlyContinue

$innerScript = @'
param($NetbiosName, $AdfsServiceAccountName, $AdfsServiceAccountPassword, $Thumbprint, $FederationServiceName, $DonePath, $LogPath)
try {
    $cred = New-Object System.Management.Automation.PSCredential(
        "$NetbiosName\$AdfsServiceAccountName",
        (ConvertTo-SecureString -String $AdfsServiceAccountPassword -AsPlainText -Force)
    )
    Install-AdfsFarm `
        -CertificateThumbprint $Thumbprint `
        -FederationServiceName $FederationServiceName `
        -FederationServiceDisplayName 'WinCert Test Lab ADFS' `
        -ServiceAccountCredential $cred
    'SUCCESS' | Set-Content -Path $DonePath
}
catch {
    $_.Exception.Message | Set-Content -Path $LogPath
    'FAILED' | Set-Content -Path $DonePath
}
'@
Set-Content -Path $innerScriptPath -Value $innerScript

$taskName = 'LabBootstrap-InstallAdfsFarm'
$taskArgs = "-NonInteractive -ExecutionPolicy Bypass -File `"$innerScriptPath`" -NetbiosName `"$NetbiosName`" -AdfsServiceAccountName `"$AdfsServiceAccountName`" -AdfsServiceAccountPassword `"$AdfsServiceAccountPassword`" -Thumbprint `"$($cert.Thumbprint)`" -FederationServiceName `"$FederationServiceName`" -DonePath `"$innerDonePath`" -LogPath `"$innerLogPath`""

& schtasks.exe /Create /TN $taskName /TR "powershell.exe $taskArgs" /SC ONCE /ST 00:00 /RU "$DomainAdminUsername" /RP "$DomainAdminPassword" /RL HIGHEST /F | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "schtasks /Create failed with exit code $LASTEXITCODE"
}

& schtasks.exe /Run /TN $taskName | Out-Null

Write-Output "Creating ADFS farm via scheduled task running as $DomainAdminUsername (this can take a few minutes)..."
$timeoutAt = (Get-Date).AddMinutes(10)
while (-not (Test-Path $innerDonePath) -and (Get-Date) -lt $timeoutAt) {
    Start-Sleep -Seconds 10
}

& schtasks.exe /Delete /TN $taskName /F | Out-Null

if (-not (Test-Path $innerDonePath)) {
    throw "Install-AdfsFarm scheduled task did not report completion within 10 minutes."
}

$result = Get-Content -Path $innerDonePath -Raw
if ($result.Trim() -ne 'SUCCESS') {
    $logContent = if (Test-Path $innerLogPath) { Get-Content -Path $innerLogPath -Raw } else { '(no log captured)' }
    throw "Install-AdfsFarm failed: $logContent"
}

# Clean up the temp script/log - they briefly held the service account
# password in plaintext on disk.
Remove-Item $innerScriptPath, $innerLogPath, $innerDonePath -ErrorAction SilentlyContinue

Write-Output "Farm created. Exporting bootstrap certificate for adfs-02..."

$exportPassword = ConvertTo-SecureString -String $CertExportPassword -AsPlainText -Force
$pfxPath = "\\$DcHostName\AdfsBootstrap`$\adfs-bootstrap.pfx"

# Small retry loop - the share may briefly be unavailable on first boot.
$attempt = 0
$exported = $false
while (-not $exported -and $attempt -lt 5) {
    try {
        Export-PfxCertificate -Cert "Cert:\LocalMachine\My\$($cert.Thumbprint)" -FilePath $pfxPath -Password $exportPassword | Out-Null
        $exported = $true
    }
    catch {
        $attempt++
        Write-Output "Export attempt $attempt failed ($($_.Exception.Message)), retrying in 15s..."
        Start-Sleep -Seconds 15
    }
}

if (-not $exported) {
    throw "Could not export the bootstrap certificate to $pfxPath after 5 attempts."
}

Write-Output "Bootstrap certificate exported to $pfxPath. adfs-01 setup complete."
