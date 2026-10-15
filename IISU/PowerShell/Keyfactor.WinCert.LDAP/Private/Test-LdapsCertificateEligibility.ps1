function Test-LdapsCertificateEligibility {
    <#
    .SYNOPSIS
    Fail-fast advisory check for whether a certificate is a plausible LDAPS (AD DS SSL) server
    certificate for the local Domain Controller.

    .DESCRIPTION
    This is Keyfactor's OWN advisory check to make Add-time errors legible to the customer - it is
    explicitly NOT an authoritative reproduction of AD DS's internal certificate-selection algorithm,
    and callers/docs must say so. Two of the rules below have real, flagged uncertainty and should be
    confirmed against current Microsoft LDAPS-certificate documentation and lab behavior before this
    is trusted to reject certificates that AD DS itself would actually have accepted:
      - Whether AD DS truly tolerates a certificate with NO EKU extension at all (vs. requiring
        Server Authentication to be explicitly present) - moderate-low confidence.
      - Whether a SAN entry matching the forest root domain's DNS name (rather than this DC's own
        FQDN) is also an accepted alternative - moderate confidence, documentation wording has
        varied across Microsoft revisions.

    FQDN is derived ONLY from local $env: variables ($env:COMPUTERNAME, $env:USERDNSDOMAIN) -
    deliberately never via Get-ADDomain/[System.DirectoryServices.ActiveDirectory.Domain] or any
    other directory-bind call, to guarantee this function cannot introduce a WinRM double-hop
    dependency when run inside a remote/JEA session (see docsource/winldap.md).
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $serverAuthOid = '1.3.6.1.5.5.7.3.1'

    # --- EKU check ---------------------------------------------------------
    $ekuExtension = $Certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
    if ($ekuExtension) {
        $hasServerAuth = $ekuExtension.EnhancedKeyUsages | Where-Object { $_.Value -eq $serverAuthOid }
        if (-not $hasServerAuth) {
            return [PSCustomObject]@{
                Eligible = $false
                Reason   = "Certificate has an Enhanced Key Usage extension that does not include Server Authentication ($serverAuthOid), which LDAPS requires."
            }
        }
    }
    # else: no EKU restriction present at all - treated as eligible (unrestricted use), per the
    # flagged assumption above.

    # --- SAN / Subject FQDN check ------------------------------------------
    $localHostName = $env:COMPUTERNAME
    $localDnsDomain = $env:USERDNSDOMAIN

    if ([string]::IsNullOrWhiteSpace($localDnsDomain)) {
        return [PSCustomObject]@{
            Eligible = $false
            Reason   = "Unable to determine the local domain name from `$env:USERDNSDOMAIN. This check requires the orchestrator process to be running in a domain security context on the target Domain Controller."
        }
    }

    $localFqdn = "$localHostName.$localDnsDomain"

    $sanText = Get-CertificateSAN $Certificate
    $sanDnsNames = @()
    if ($sanText) {
        # @(...) forces an array even when there is exactly one match - without it, a single match
        # collapses to a scalar string and "$sanDnsNames + $subjectCn" below silently becomes string
        # concatenation instead of array concatenation.
        $sanDnsNames = @([regex]::Matches($sanText, 'DNS Name\s*=\s*([^,;]+)') | ForEach-Object { $_.Groups[1].Value.Trim() })
    }

    $subjectCn = $null
    if ($Certificate.Subject -match 'CN=([^,]+)') {
        $subjectCn = $matches[1].Trim()
    }

    $candidateNames = @($sanDnsNames + $subjectCn) | Where-Object { $_ }

    $matchesLocalFqdn = $candidateNames | Where-Object { $_ -ieq $localFqdn }
    $matchesForestRootDomain = $candidateNames | Where-Object { $_ -ieq $localDnsDomain }

    if (-not $matchesLocalFqdn -and -not $matchesForestRootDomain) {
        return [PSCustomObject]@{
            Eligible = $false
            Reason   = "Certificate Subject/SAN ($($candidateNames -join ', ')) does not include this Domain Controller's FQDN ('$localFqdn') or the domain name ('$localDnsDomain')."
        }
    }

    return [PSCustomObject]@{
        Eligible = $true
        Reason   = ""
    }
}
