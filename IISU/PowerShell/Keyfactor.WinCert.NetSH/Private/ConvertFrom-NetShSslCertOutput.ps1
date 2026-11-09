# ConvertFrom-NetShSslCertOutput parses the text output of "netsh http show sslcert" into objects.
#
# UNVERIFIED ASSUMPTION (see docs/winnetsh-implementation-notes.md): "netsh http show sslcert" has no
# structured (JSON/XML/CSV) output mode on any supported Windows version - only this fixed-width,
# localized-label text block. The parser below assumes each binding is rendered as a block of
# "Label<padding>: Value" lines separated from other bindings by a blank line, and that the label
# column is always padded with two or more spaces before the separating colon (this is what
# distinguishes the separator from a colon that is part of the label itself, e.g. "IP:port"). This
# has only been verified against one Windows Server version as of this writing - re-verify against
# every OS version this store type is deployed to.
function ConvertFrom-NetShSslCertOutput {
    [CmdletBinding()]
    [OutputType([pscustomobject[]])]
    param (
        # AllowEmptyCollection permits an empty array; AllowEmptyString is also required because
        # netsh's output legitimately contains blank-line elements (used as block separators below) -
        # without it, Mandatory validation rejects the entire array if even one element is "".
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$RawOutput
    )

    $blocks = @()
    $current = New-Object System.Collections.Generic.List[string]

    foreach ($line in $RawOutput) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($current.Count -gt 0) {
                $blocks += , @($current)
                $current = New-Object System.Collections.Generic.List[string]
            }
            continue
        }

        # Only keep lines that look like "Label<2+ spaces>: Value" - this skips banner/header lines
        # such as "SSL Certificate bindings:" and the "-------" underline netsh prints above the list.
        if ($line -match '^\s*\S.*?\s{2,}:\s?.*$') {
            $current.Add($line)
        }
    }
    if ($current.Count -gt 0) { $blocks += , @($current) }

    $bindings = foreach ($block in $blocks) {
        $props = [ordered]@{}
        foreach ($line in $block) {
            if ($line -match '^\s*(?<label>\S.*?)\s{2,}:\s?(?<value>.*)$') {
                $props[$Matches['label'].Trim()] = $Matches['value'].Trim()
            }
        }
        if ($props.Count -eq 0) { continue }

        $certificateHash = $null
        if ($props.Contains('Certificate Hash')) {
            $certificateHash = ($props['Certificate Hash'] -replace '\s', '').ToUpperInvariant()
        }

        # A block with no certificate hash isn't a real sslcert binding (defensive - every real
        # binding block has one), so skip it rather than returning a half-populated object.
        if ([string]::IsNullOrEmpty($certificateHash)) { continue }

        [PSCustomObject]@{
            IPPort               = if ($props.Contains('IP:port')) { $props['IP:port'] } else { $null }
            HostnamePort         = if ($props.Contains('Hostname:port')) { $props['Hostname:port'] } else { $null }
            CertificateHash      = $certificateHash
            ApplicationId        = if ($props.Contains('Application ID')) { $props['Application ID'] } else { $null }
            CertificateStoreName = if ($props.Contains('Certificate Store Name')) { $props['Certificate Store Name'] } else { $null }
        }
    }

    return @($bindings)
}
