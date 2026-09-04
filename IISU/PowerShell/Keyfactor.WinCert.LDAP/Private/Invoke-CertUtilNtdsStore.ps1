function Invoke-CertUtilNtdsStore {
    <#
    .SYNOPSIS
    Runs certutil.exe against a Windows service-specific certificate store (e.g. the NTDS/LDAPS
    store) and returns its stdout/stderr/exit code.

    .DESCRIPTION
    certutil.exe's "-service" switch is a store-LOCATION option (like -enterprise, -user or
    -grouppolicy), not a verb parameter that takes the service name as its own argument. It must
    appear among the leading options, and the service store is then named as a SINGLE
    "<ServiceName>\<StoreName>" token in the store-name position:
        certutil [-f] -service -store    NTDS\My [<Thumbprint>]
        certutil [-f] -service -addstore NTDS\My <CertFile>
        certutil [-f] -service -delstore NTDS\My <Thumbprint>
    Passing the service name and store name as two separate positional arguments makes certutil see
    an extra parameter and fail with 0x80070057 (ERROR_INVALID_PARAMETER).

    The callers (Get/Set/Remove-NtdsServiceStoreCertificate) treat this as an opaque "run certutil,
    get text back" boundary.

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
