#Requires -RunAsAdministrator
<#
    Runs on adfs-02 as Local System. Installs the AD FS role, imports the
    same bootstrap certificate adfs-01 exported to the dc-01 handoff
    share, and joins the existing farm.

    Add-AdfsFarmNode needs to run as a domain admin, same as
    Install-AdfsFarm on adfs-01 - see the long comment in
    Bootstrap-AdfsPrimary.ps1 for why this uses a short-lived scheduled
    task to impersonate one rather than the runCommand resource's own
    (broken, in testing) runAsUser feature.

    Idempotent: if this node is already a member of the farm, the script
    exits without changing anything. Depends (via main.bicep's dependsOn)
    on adfs-01's Bootstrap-AdfsPrimary.ps1 run having already completed -
    this script also retries for a while in case that dependency ordering
    doesn't fully guarantee the farm/certificate are ready the instant
    this one starts.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $NetbiosName,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountName,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountPassword,

    [Parameter(Mandatory = $true)]
    [string] $Adfs01HostName,

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

$adminGroupMember = Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "*\$AdfsServiceAccountName" }
if (-not $adminGroupMember) {
    Add-LocalGroupMember -Group 'Administrators' -Member "$NetbiosName\$AdfsServiceAccountName"
}

$alreadyJoined = $false
try {
    $service = Get-Service -Name adfssrv -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Get-AdfsFarmInformation -ErrorAction Stop | Out-Null
        $alreadyJoined = $true
    }
}
catch {
    $alreadyJoined = $false
}

if ($alreadyJoined) {
    Write-Output "This node is already part of the ADFS farm - skipping join."
    exit 0
}

$pfxPath = "\\$DcHostName\AdfsBootstrap`$\adfs-bootstrap.pfx"

Write-Output "Waiting for the bootstrap certificate exported by adfs-01 to appear at $pfxPath..."
$attempt = 0
$found = $false
while (-not $found -and $attempt -lt 20) {
    if (Test-Path $pfxPath) {
        $found = $true
    }
    else {
        $attempt++
        Start-Sleep -Seconds 30
    }
}

if (-not $found) {
    throw "Bootstrap certificate never appeared at $pfxPath. Confirm adfs-01's Bootstrap-AdfsPrimary.ps1 run command succeeded before retrying this one."
}

Write-Output "Importing bootstrap certificate..."
$exportPassword = ConvertTo-SecureString -String $CertExportPassword -AsPlainText -Force
$importedCert = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation 'Cert:\LocalMachine\My' -Password $exportPassword -Exportable

Write-Output "Imported certificate thumbprint: $($importedCert.Thumbprint)"

# --- Run Add-AdfsFarmNode as a domain admin via a short-lived scheduled task ---
$workDir = 'C:\LabBootstrap'
New-Item -Path $workDir -ItemType Directory -Force | Out-Null
$innerScriptPath = Join-Path $workDir 'join-adfs-farm.ps1'
$innerLogPath = Join-Path $workDir 'join-adfs-farm.log'
$innerDonePath = Join-Path $workDir 'join-adfs-farm.done'
Remove-Item $innerDonePath, $innerLogPath -ErrorAction SilentlyContinue

$innerScript = @'
param($NetbiosName, $AdfsServiceAccountName, $AdfsServiceAccountPassword, $Thumbprint, $PrimaryComputerName, $DonePath, $LogPath)
try {
    $cred = New-Object System.Management.Automation.PSCredential(
        "$NetbiosName\$AdfsServiceAccountName",
        (ConvertTo-SecureString -String $AdfsServiceAccountPassword -AsPlainText -Force)
    )
    Add-AdfsFarmNode `
        -PrimaryComputerName $PrimaryComputerName `
        -CertificateThumbprint $Thumbprint `
        -ServiceAccountCredential $cred
    'SUCCESS' | Set-Content -Path $DonePath
}
catch {
    $_.Exception.Message | Set-Content -Path $LogPath
    'FAILED' | Set-Content -Path $DonePath
}
'@
Set-Content -Path $innerScriptPath -Value $innerScript

$taskName = 'LabBootstrap-JoinAdfsFarm'
$taskArgs = "-NonInteractive -ExecutionPolicy Bypass -File `"$innerScriptPath`" -NetbiosName `"$NetbiosName`" -AdfsServiceAccountName `"$AdfsServiceAccountName`" -AdfsServiceAccountPassword `"$AdfsServiceAccountPassword`" -Thumbprint `"$($importedCert.Thumbprint)`" -PrimaryComputerName `"$Adfs01HostName`" -DonePath `"$innerDonePath`" -LogPath `"$innerLogPath`""

& schtasks.exe /Create /TN $taskName /TR "powershell.exe $taskArgs" /SC ONCE /ST 00:00 /RU "$DomainAdminUsername" /RP "$DomainAdminPassword" /RL HIGHEST /F | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "schtasks /Create failed with exit code $LASTEXITCODE"
}

& schtasks.exe /Run /TN $taskName | Out-Null

Write-Output "Joining ADFS farm via primary node $Adfs01HostName, using scheduled task running as $DomainAdminUsername (this can take a few minutes)..."
$timeoutAt = (Get-Date).AddMinutes(10)
while (-not (Test-Path $innerDonePath) -and (Get-Date) -lt $timeoutAt) {
    Start-Sleep -Seconds 10
}

& schtasks.exe /Delete /TN $taskName /F | Out-Null

if (-not (Test-Path $innerDonePath)) {
    throw "Add-AdfsFarmNode scheduled task did not report completion within 10 minutes."
}

$result = Get-Content -Path $innerDonePath -Raw
if ($result.Trim() -ne 'SUCCESS') {
    $logContent = if (Test-Path $innerLogPath) { Get-Content -Path $innerLogPath -Raw } else { '(no log captured)' }
    throw "Add-AdfsFarmNode failed: $logContent"
}

Remove-Item $innerScriptPath, $innerLogPath, $innerDonePath -ErrorAction SilentlyContinue

Write-Output "adfs-02 joined the farm successfully."
