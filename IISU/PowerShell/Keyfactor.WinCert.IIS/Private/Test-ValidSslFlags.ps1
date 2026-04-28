function Test-ValidSslFlags {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Flags,
        
        [Parameter(Mandatory = $false)]
        [switch]$ThrowOnError
    )
    
    $build = [System.Environment]::OSVersion.Version.Build
    $validBits = Get-ValidSslFlagsForSystem
    
    # Calculate valid bitmask
    $validMask = 0
    foreach ($bit in $validBits) {
        $validMask = $validMask -bor $bit
    }
    
    # Check for unknown/unsupported bits
    $unknownBits = $Flags -band (-bnot $validMask)
    if ($unknownBits -ne 0) {
        $errorMsg = "SslFlags value $Flags (0x$($Flags.ToString('X'))) contains unsupported bits " +
                    "for this Windows Server version (Build: $build): $unknownBits (0x$($unknownBits.ToString('X'))). " +
                    "Supported flags: $($validBits -join ', ')"
        
        if ($ThrowOnError) {
            throw $errorMsg
        }
        else {
            return [PSCustomObject]@{
                IsValid = $false
                ErrorCode = 400
                Message = $errorMsg
                WindowsBuild = $build
                ValidFlags = $validBits
                InvalidBits = $unknownBits
            }
        }
    }
    
    # Check for known invalid combinations
    $hasSni = ($Flags -band 1) -ne 0
    $hasCentralCert = ($Flags -band 2) -ne 0
    
    if ($hasCentralCert -and -not $hasSni) {
        $errorMsg = "SslFlags value $Flags (0x$($Flags.ToString('X'))) is invalid: " +
                    "CentralCertStore (0x2) requires SNI (0x1) to be enabled."
        
        if ($ThrowOnError) {
            throw $errorMsg
        }
        else {
            return [PSCustomObject]@{
                IsValid = $false
                ErrorCode = 400
                Message = $errorMsg
                WindowsBuild = $build
                ValidFlags = $validBits
                InvalidBits = 0
            }
        }
    }
    
    # Validation passed
    $successMsg = "SslFlags value $Flags (0x$($Flags.ToString('X'))) is valid for this system (Build: $build)."
    
    if ($ThrowOnError) {
        Write-Verbose $successMsg
        return $true
    }
    else {
        return [PSCustomObject]@{
            IsValid = $true
            ErrorCode = 0
            Message = $successMsg
            WindowsBuild = $build
            ValidFlags = $validBits
            InvalidBits = 0
        }
    }
}