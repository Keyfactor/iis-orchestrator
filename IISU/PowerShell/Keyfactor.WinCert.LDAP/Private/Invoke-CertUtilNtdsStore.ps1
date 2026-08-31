function Invoke-CertUtilNtdsStore {
    <#
    .SYNOPSIS
    Runs certutil.exe against a Windows service-specific certificate store (e.g. the NTDS/LDAPS
    store) and returns its stdout/stderr/exit code.

    .DESCRIPTION
    UNVERIFIED - MUST BE LAB-VALIDATED ON A REAL DOMAIN CONTROLLER BEFORE THIS SHIPS.
    certutil.exe has a long-documented "-service" switch for targeting service-specific certificate
    stores (used historically for NTDS, RPC, MSMQ, etc.), separate from the plain "-store"/
    "-addstore"/"-delstore" verbs that target the ordinary LocalMachine store. This function assumes
    the argument shape is:
        certutil [-f] -store    -service <ServiceName> <StoreName> [<Thumbprint>]
        certutil [-f] -addstore -service <ServiceName> <StoreName> <CertFile>
        certutil [-f] -delstore -service <ServiceName> <StoreName> <Thumbprint>
    This exact ordering/casing has NOT been confirmed against a live NTDS service store in this
    session. If lab testing shows a different argument shape, only this function and its callers'
    argument-building should need to change - the callers (Get/Set/Remove-NtdsServiceStoreCertificate)
    treat this as an opaque "run certutil, get text back" boundary.

    Invoked via .NET ProcessStartInfo (not the PowerShell pipeline), matching the existing
    Add-KeyfactorCertificate.ps1 pattern in Keyfactor.WinCert.Common - this means certutil.exe does
    NOT need a VisibleExternalCommands entry in the JEA RoleCapabilities file.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $argLine = ($Arguments | ForEach-Object {
        if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
    }) -join ' '

    Write-Information "[VERBOSE] Running certutil against service store with arguments: $argLine"

    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName               = "certutil.exe"
    $processInfo.Arguments              = $argLine.Trim()
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError  = $true
    $processInfo.UseShellExecute        = $false
    $processInfo.CreateNoWindow         = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $processInfo

    try {
        [void]$process.Start()
    }
    catch {
        return [PSCustomObject]@{
            Started  = $false
            ExitCode = -1
            StdOut   = ""
            StdErr   = "Failed to launch certutil.exe: $($_.Exception.Message)"
        }
    }

    $stdOut = $process.StandardOutput.ReadToEnd()
    $stdErr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return [PSCustomObject]@{
        Started  = $true
        ExitCode = $process.ExitCode
        StdOut   = $stdOut
        StdErr   = $stdErr
    }
}
