function Get-ValidSslFlagsForSystem {
    <#
    .SYNOPSIS
    Gets the valid SSL flag bits for the current Windows Server version
    #>
    [CmdletBinding()]
    param()
    
    $build = [System.Environment]::OSVersion.Version.Build
    
    # Return array of valid flag values based on Windows Server version
    if ($build -ge 20348) {
        # Windows Server 2022+ (IIS 10.0.20348+)
        Write-Verbose "Detected Windows Server 2022 or later (Build: $build)"
        return @(1, 4, 8, 16, 32, 64)  # Include unknowns for testing
    }
    elseif ($build -ge 17763) {
        # Windows Server 2019 (IIS 10.0.17763)
        Write-Verbose "Detected Windows Server 2019 (Build: $build)"
        return @(1, 4, 8)
    }
    elseif ($build -ge 14393) {
        # Windows Server 2016 (IIS 10.0)
        Write-Verbose "Detected Windows Server 2016 (Build: $build)"
        return @(1, 4)
    }
    else {
        # Windows Server 2012 R2 and earlier (IIS 8.5)
        Write-Verbose "Detected Windows Server 2012 R2 or earlier (Build: $build)"
        return @(1, 2)
    }
}