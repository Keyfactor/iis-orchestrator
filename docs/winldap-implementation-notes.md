# WinLDAP Store Type — Implementation Notes

Working notes for the WinLDAP store type (AD DS / NTDS LDAPS certificate management), covering why
it was built this way, exactly what changed, and what still needs to be validated on a real Domain
Controller. Written to travel with the branch/repo across machines (e.g. moving from a dev machine
to one with lab DC access) - see `docs/winldap-ntds-validation.ps1` for the hands-on validation
script referenced below.

## Why

A customer manually renews their Domain Controllers' LDAPS (AD DS "LDAP over SSL", port 636) server
certificate by (1) importing it into the Personal ("My") store so AD DS can detect it, then
(2) manually copying/registering it into the NTDS-service-specific certificate store (registry-backed
at `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates`),
which is what the LDAPS listener actually reads from. WinLDAP automates that two-step process as a
new Keyfactor Universal Orchestrator store type, following the same architecture as the existing
`WinCert`/`WinSQL`/`WinAdfs` store types in this repo.

## Design decisions

- **Each Domain Controller is its own independent certificate store** (like `WinCert`), not a farm
  fan-out model like `WinAdfs`'s `AdfsCertificateRotationManager` - every DC's LDAPS certificate has
  its own unique Subject/SAN.
- **Both local-agent (`|LocalMachine`) and remote WinRM/JEA are supported** - a deliberate departure
  from `WinAdfs`'s local-only restriction. `WinAdfs` is forced local-only because it fans out to
  *secondary* farm nodes, and a remotely-connected primary session can't safely delegate credentials
  to open further WinRM sessions to those secondary nodes (the classic double-hop problem). WinLDAP
  has no fan-out - every operation touches only the one DC already connected to - so there's no
  second hop to make, as long as the implementation never calls `ActiveDirectory`-module cmdlets or
  anything else that would reach a second machine. `WinSQL` already ships this exact "local
  registry/store work over ordinary remote WinRM" pattern in production.
- **Inventory and Remove are scoped strictly to the NTDS service store** (the single source of
  truth), not a two-location model. The Personal-store copy created during Add is treated as an
  internal staging detail, surfaced only as a diagnostic warning on mismatch, never as inventory data.
- **Add sequence**: fail-fast eligibility check (Server-Auth EKU + FQDN/domain SAN match, derived
  only from local `$env:` variables - never a directory query, to guarantee no double-hop) → stage
  into `LocalMachine\My` via the existing, unmodified `Add-KeyfactorCertificate` → explicitly write
  the same certificate into the NTDS service store (does not wait for/rely on AD DS's own ~10-minute
  auto-detection) → optional, off-by-default `Restart-Service NTDS` to force LDAPS to pick it up
  immediately.
- **Add/Remove only for the initial release** - no ReEnrollment or Discovery, matching the
  constrained-scope precedent set by `WinAdfs`.

Full rationale and alternatives considered are in the planning transcript; this file is the
condensed, durable version of that reasoning.

## What changed

**New:**
- `IISU/ImplementedStoreTypes/WinLDAP/Inventory.cs`, `Management.cs`, `WinLdapCertificateInfo.cs` -
  C# job classes, mirroring the `WinCert`/`WinSQL` pattern (`PSHelper` construction, PS function
  dispatch, `ResultObject` parsing).
- `IISU/PowerShell/Keyfactor.WinCert.LDAP/` - new PowerShell module:
  - `Public/Get-KeyfactorLdapCertificates.ps1`, `Add-KeyfactorLdapsCertificate.ps1`,
    `Remove-KeyfactorLdapsCertificate.ps1`
  - `Private/Invoke-CertUtilNtdsStore.ps1`, `Get-NtdsServiceStoreCertificate.ps1`,
    `Set-NtdsServiceStoreCertificate.ps1`, `Remove-NtdsServiceStoreCertificate.ps1`,
    `Test-LdapsCertificateEligibility.ps1`
  - `RoleCapabilities/Keyfactor.WinCert.LDAP.psrc` - JEA role capability (no
    `VisibleExternalCommands` entry needed; certutil is invoked via `ProcessStartInfo`, matching the
    existing `Add-KeyfactorCertificate.ps1` pattern)
  - `Keyfactor.WinCert.LDAP.psm1` - module loader/exports
- `docsource/winldap.md` - short store-type overview (stitched into the generated `README.md`)
- `docs/winldap-implementation-notes.md` (this file) and `docs/winldap-ntds-validation.ps1`

**Modified:**
- `integration-manifest.json` - new `WinLDAP` store type entry (Add/Remove only, fixed
  `StorePathValue: "NTDS\\My"`, `PrivateKeyAllowed: "Required"`, `BlueprintAllowed: false`, reuses
  the existing base Properties set plus the already-shared `RestartService` property)
- `IISU/manifest.json` - `CertStores.WinLDAP.Inventory`/`.Management` type-mapping entries
- `IISU/WindowsCertStore.csproj` - copy-to-output and folder-include entries for the new PowerShell
  module folder
- `CHANGELOG.md` - new "Unreleased" entry (version number intentionally left to the maintainers)

**Explicitly not modified** (no special-casing needed, unlike `WinAdfs`'s built-in-module import
branch): `IISU/PSHelper.cs`, `IISU/Models/JobProperties.cs`, `IISU/WinCertJobTypeBase.cs`.

## Status as of this writing

- Solution builds clean (0 errors) on `net8.0`/`net10.0`.
- All existing unit tests pass except one pre-existing, unrelated failure
  (`AdfsUnitTests.Test_AdfsInventory`, confirmed to fail identically on a clean checkout without any
  WinLDAP changes - an environment/mocking issue in that test, not a regression).
- `Test-LdapsCertificateEligibility` was smoke-tested locally against in-memory self-signed
  certificates (matching/non-matching FQDN, present/absent EKU, wrong EKU) - one real bug was found
  and fixed during that testing: `[regex]::Matches(...) | ForEach-Object {...}` collapses to a
  scalar when there's exactly one match, which silently turned an array-concatenation into a
  string-concatenation. Fixed by wrapping in `@(...)`.
- **Nothing involving the actual NTDS service store has been validated** - this repo/session has no
  access to a real Domain Controller. See the next section.

## Known unverified assumptions - must be lab-validated

These are also flagged inline in the code (search for "UNVERIFIED" in
`IISU/PowerShell/Keyfactor.WinCert.LDAP/`), collected here as a single checklist:

1. The exact `certutil -store`/`-addstore`/`-delstore -service NTDS My` argument syntax
   (`Invoke-CertUtilNtdsStore.ps1` and its callers).
2. Whether a certificate written into the NTDS service store as just a `.cer` (public bytes only,
   no PFX re-import) resolves `HasPrivateKey = true` when read back - i.e. whether Windows resolves
   the private-key association via machine-key-container matching independent of which logical store
   lists the certificate (`Set-NtdsServiceStoreCertificate.ps1`'s core assumption).
3. Whether `Cert:\LocalMachine\Services\NTDS\My` exists as a PowerShell provider path under Windows
   PowerShell 5.1 (probably not, but not confirmed) - not currently relied upon, but worth ruling out
   as a cleaner alternative to certutil text-parsing.
4. **The most important one**: whether the ACLs on
   `HKLM\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates` permit writes from a JEA
   virtual account with local-Administrator-equivalent rights (this repo's existing JEA model), or
   whether it requires `SYSTEM`/the NTDS service's own security context. If this fails, remote
   JEA-based management may not be viable and local-agent-only (running as the Universal Orchestrator
   service's own account) may be the only option.
5. Whether the LDAPS listener picks up a newly-written NTDS-store certificate immediately, only
   after `Restart-Service NTDS`, or only after a reboot.
6. Whether AD DS's own built-in automatic Personal-store certificate detection could later overwrite
   what WinLDAP wrote directly to the NTDS store, since the certificate also remains staged in
   Personal.
7. The operational effect of **removing** the certificate currently active on port 636 (stops
   responding vs. falls back vs. needs a restart to notice).
8. End-to-end confirmation that a real remote WinRM+JEA session introduces no double-hop failure
   (e.g. inspect `klist` inside the session).
9. Whether the eligibility validator's rules (`Test-LdapsCertificateEligibility.ps1`) - Server-Auth
   EKU tolerance when absent, forest-root-domain SAN as an alternative to the DC's own FQDN - match
   real AD DS selection behavior closely enough to avoid false rejections.

## Next step

Run `docs/winldap-ntds-validation.ps1` interactively, section by section, against a disposable test
certificate on a lab Domain Controller. It walks through items 1, 2, 4, 5, 6, and 7 above directly;
items 3, 8, and 9 are called out in its comments for separate follow-up. Update this file with
findings once that's done - particularly items 2 and 4, since either one failing would force a
design change in `Set-NtdsServiceStoreCertificate.ps1` or the local-agent-vs-remote recommendation.
