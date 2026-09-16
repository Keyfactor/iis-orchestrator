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
- **Both local-agent (`|LocalMachine`) and remote WinRM/JEA/SSH are supported**, following the same
  unconditional pattern as `WinSQL` (see "Reversed on 2026-09-03" below) - WinLDAP has no farm
  fan-out, unlike `WinAdfs`, so there's no double-hop concern in principle, and the implementation
  never calls `ActiveDirectory`-module cmdlets or anything else that would reach a second machine.
  This was briefly narrowed to local-only pending lab-validation of JEA-account registry ACLs on a
  hardened DC, then reversed at the requester's direction after discussion with stakeholders - that
  ACL question remains genuinely unverified and is tracked in "Remaining unverified assumptions."
- **Inventory is scoped strictly to the NTDS service store** (the single source of truth for what's
  "in" this store); the Personal-store copy created during Add is treated as an internal staging
  detail there, surfaced only as a diagnostic warning on mismatch, never as inventory data. **Remove
  is not scoped that way** - see "Resolved during lab validation (2026-09-16)" below: it removes the
  certificate from both the NTDS store and the Personal store, because lab testing showed the
  Personal-store copy has to go too for LDAPS to actually stop using the certificate. This was the
  original design (a symmetric "Remove only touches NTDS, like Inventory only reads NTDS" rule) and
  it did not hold up under testing.
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
  - `Private/Get-NtdsServiceStoreCertificate.ps1`, `Set-NtdsServiceStoreCertificate.ps1`,
    `Remove-NtdsServiceStoreCertificate.ps1`, `Test-LdapsCertificateEligibility.ps1` - the NTDS-store
    read/write/delete functions operate directly on the registry (see "Resolved during lab
    validation" below); no certutil.exe dependency.
  - `RoleCapabilities/Keyfactor.WinCert.LDAP.psrc` - JEA role capability, mirroring
    `Keyfactor.WinCert.SQL.psrc` (no `VisibleExternalCommands` needed, since the registry/`.NET`
    mechanism uses no external processes).
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
- The NTDS registry read/write/delete mechanism itself was verified against a live, registry-backed
  service store on a machine running NTDS (see "Resolved during lab validation" below), but the
  full Add/Remove/Inventory flow through this store type's PowerShell functions has not yet been
  re-run end-to-end against a real DC since that mechanism was rewritten - do that before
  considering this production-ready (see `docs/winldap-ntds-validation.ps1` and the Verification
  section of the plan this was implemented from). The solution should also be rebuilt and the unit
  tests re-run after these changes.

## Resolved during lab validation (2026-09-01)

Lab testing on a real Domain Controller falsified two of the original candidate mechanisms and
prompted a scope change, discussed and agreed with the requester:

1. **`certutil -addstore`/`-delstore -service NTDS My` does not work at all.** `certutil -addstore
   -?` / `-delstore -?` help text confirms `-service` is not a supported switch on those two verbs
   in this certutil build - only the read-only `-store` verb documents it. This is what produced the
   `ERROR_INVALID_PARAMETER` (0x80070057) seen in lab testing, not an argument-order mistake.
2. **`Cert:\LocalMachine\Services\NTDS\My` does not exist** as a PowerShell provider path (confirmed
   via `Test-Path` on a live DC), ruling out candidate #2 as suspected.
3. **Replacement mechanism: direct registry write**, not certutil and not a P/Invoke helper DLL. The
   registry-backed store format was verified, not guessed: each certificate is a subkey of
   `...\SystemCertificates\<StoreName>\Certificates` named by its uppercase SHA1 thumbprint, holding
   one `REG_BINARY` value named `Blob`, and that `Blob` is byte-for-byte identical to
   `[X509Certificate2]::Export([X509ContentType]::SerializedCert)` - a documented, supported .NET
   export format. `Get`/`Set`/`Remove-NtdsServiceStoreCertificate.ps1` now use this directly;
   `Invoke-CertUtilNtdsStore.ps1` was deleted, along with the certutil `VisibleExternalCommands`
   dependency. A P/Invoke `CertOpenStore` helper DLL was considered and rejected - the requester
   noted customers are generally unwilling to have new DLLs/executables added to their machines
   (JEA itself was already a tolerated exception for other store types, but a DC is a harder sell).
4. The full `Add-KeyfactorLdapsCertificate` flow (load PFX with `PersistKeySet`/`MachineKeySet` →
   stage into `Cert:\LocalMachine\My` → write to the NTDS-style registry path → read back) was
   functionally tested end-to-end, twice, under two different PowerShell runtimes, because the two
   runs disagreed and the discrepancy mattered:
   - Under **PowerShell 7 (Core edition, `pwsh`)**: an `X509Certificate2` loaded directly from PFX
     bytes did NOT reliably carry `HasPrivateKey = true`, even with `PersistKeySet`/`MachineKeySet`,
     and exporting `SerializedCert` from it produced a blob that read back `HasPrivateKey = false`.
   - Under **Windows PowerShell 5.1 (Desktop/.NET Framework)** - the actual runtime `PSHelper.cs`
     launches for local execution (`PowerShellProcessInstance(new Version(5, 1), ...)`,
     `PSHelper.cs:491`) - the same flow worked correctly at every step: the PFX-loaded certificate
     already showed `HasPrivateKey = true`, the `SerializedCert` blob written to a registry path
     shaped like the real NTDS store read back with `HasPrivateKey = true`, and `Get-CertificateCSP`
     correctly identified the provider ("Microsoft Software Key Storage Provider").

   Since production only ever runs this locally under Windows PowerShell 5.1, the Core-edition
   discrepancy doesn't apply to production - but it's why `Add-KeyfactorLdapsCertificate.ps1` now
   has an explicit "RereadPersonal" step that re-reads the certificate from
   `Cert:\LocalMachine\My` after staging (via the `Cert:` provider) before writing to the NTDS
   store, rather than passing the original PFX-loaded object straight through. That is
   unnecessary for correctness under the real runtime (confirmed above) but is a cheap extra
   checkpoint that also protects against the same failure mode if this module is ever run under
   PowerShell 7 for any reason. This substantially de-risks former assumption #2 (private-key
   association resolves independent of which store lists the certificate) ahead of DC testing,
   though final confirmation still requires reading `HasPrivateKey` back from the actual NTDS
   store on a live DC - ACLs on the private key itself, readable by whichever account NTDS/the
   orchestrator agent runs as, are a separate variable this test can't rule out.
5. **Scope narrowed to local-agent-only for this release.** Not for `WinAdfs`'s reason
   (double-hop avoidance during farm fan-out - doesn't apply here, each DC is independent). The
   reason here: DCs are Tier-0, many hardened AD shops disable inbound WinRM to DCs categorically
   anyway, and the one JEA-specific risk that actually matters - whether a JEA virtual account's
   ACLs permit writes to `HKLM\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates` -
   is still unverified with no cheap way to check it without a hardened lab DC. Shipping and
   supporting a remote/JEA path that might silently fail on that permission boundary was judged
   worse than being explicit about the limitation. `Management.cs`/`Inventory.cs` now fail fast with
   a clear message if `PSHelper.IsLocalMachine` is false, rather than attempting an unsupported path.
   The `RoleCapabilities/Keyfactor.WinCert.LDAP.psrc` JEA role-capability file was deleted as a
   result - it documented the now-dead certutil mechanism and an out-of-scope JEA path.

## Reversed on 2026-09-03

After discussing with peers, the requester asked to restore remote WinRM/JEA/SSH support, following
the exact pattern already used by `WinSQL` and the other store types (including the Linux-container-
via-SSH connection model). This reverses point 5 above. **It does not resolve the underlying
registry-ACL question** - that risk is still unverified and is restored to "Remaining unverified
assumptions" below rather than being dropped.

What changed to restore this:
- Removed the `IsLocalMachine` fail-fast guards from `Management.cs`/`Inventory.cs` - confirmed by
  reading `WinSQL`'s equivalent files that they have **no** such guard; they construct `PSHelper`
  with whatever protocol/JEA settings came from job properties and proceed unconditionally, so
  WinLDAP now matches that precedent exactly.
- Re-created `RoleCapabilities/Keyfactor.WinCert.LDAP.psrc`, mirroring `Keyfactor.WinCert.SQL.psrc`
  (`ModulesToImport`, `VisibleFunctions`, the shared generic `VisibleCmdlets` list). Its
  `VisibleExternalCommands` is empty, unlike SQL's (which needs `icacls.exe`) - WinLDAP's NTDS
  mechanism is pure registry + `X509Certificate2`, no external processes.
- Confirmed `IISU/PSHelper.cs` already generically supports local/WinRM/SSH/JEA for any store type
  (`ClientMachineName` setter for locality detection, the local+JEA ambiguity guard, the SSH-vs-WinRM
  branch in `InitializeRemoteSession()`, and the hard JEA-over-SSH rejection) - **no changes needed**
  to `PSHelper.cs`, `JobProperties.cs`, or `integration-manifest.json`; WinLDAP's manifest entry
  already declared the same `WinRM Protocol`/`Port`/`JEAEndpointName` properties as `WinSQL`.
- Updated the shared `docsource/content.md` (the generic JEA setup/troubleshooting doc stitched into
  the generated README for every store type - not `docsource/wincert.md`, which is a short,
  unrelated cert-verification doc) to add WinLDAP to the module table, the RoleCapabilities
  combination table, the JEA Module Requirements table, and the Security/Permission Considerations
  registry-permission list, plus a new "Important Notes and Limitations" bullet flagging the
  DC/Tier-0-specific caveat (blanket WinRM-disable policies, the unverified JEA-account ACL question)
  and recommending `Get-KeyfactorDiagnostics` + a real Add/Remove round-trip before production use.
- Updated `IISU/PowerShell/Build/KeyfactorWinCert.pssc`'s comments (not its active `RoleDefinitions`)
  to list `Keyfactor.WinCert.LDAP` as an available capability and example combination - left the
  active default example (Common+IIS+SQL) unchanged, since WinLDAP is DC-specific and most `.pssc`
  deployments target non-DC servers.
- Restored `docsource/winldap.md`'s Requirements section to describe both connection models, with
  the Tier-0 caveats framed as "validate before production" rather than a blanket prohibition.

## Resolved during lab validation (2026-09-16)

**Removing only the NTDS-store registry entry does not stop LDAPS from using the certificate - the
Personal-store copy has to be removed too, and no service restart is required once it is.**

Sequence of tests run on a live Domain Controller, using the store type's actual Remove logic (not
just the raw registry mechanism):

1. Ran Remove (as it existed before this fix): NTDS registry entry deleted successfully, confirmed
   by re-running Inventory (correctly returned no certificates - the code was working exactly as
   designed). The Personal-store (`Cert:\LocalMachine\My`) copy was left in place, per the original
   design.
2. Connected via `ldp.exe` against port 636 - the certificate was still being presented and the
   connection still succeeded.
3. Restarted the NTDS service (`Restart-Service NTDS -Force`) with the Personal-store copy still in
   place, then retried `ldp.exe` - **still connected using the same certificate**. This rules out
   simple Schannel/lsass in-memory caching as the sole explanation (the leading hypothesis going
   into this test - restarting the service should have forced a fresh read of whatever store LDAPS
   actually consults, if the NTDS-store removal alone were sufficient).
4. Manually removed the certificate from `Cert:\LocalMachine\My`, with **no service restart** -
   `ldp.exe` immediately could no longer connect using that certificate.

This confirms LDAPS's certificate resolution depends on presence in the Personal store, not solely
on the NTDS-service-store registry entry - contrary to the original design's assumption that the
NTDS store was the definitive, sufficient location to control. `Remove-KeyfactorLdapsCertificate.ps1`
now removes from both stores in one call (NTDS store first, then Personal via the existing,
unmodified `Remove-KeyfactorCertificate` from `Keyfactor.WinCert.Common`); a failure to remove from
Personal is now a hard error (code 730), not a silently-incomplete success, since leaving it behind
reproduces exactly the bug found here. A "certificate not found in Personal" result (e.g. it was
already removed by other means) is treated as success, not an error, since the end state is already
what Remove is trying to achieve.

**New caveat introduced by this fix, not previously applicable**: `Cert:\LocalMachine\My` is a
general-purpose store shared by other services on the same Domain Controller (WinRM HTTPS, RDP,
etc.). WinLDAP's Remove has no visibility into whether the certificate it's deleting from Personal
is also relied upon by one of those other services - it will remove it regardless. This is
documented in `docsource/winldap.md` and in the function's own docstring; it has not been mitigated
(e.g. with a "check other known consumers first" step, the way `Remove-KeyfactorIISCertificateIfUnused`
checks for other IIS bindings before deleting a certificate) - that would require WinLDAP to have
awareness of other store types' state on the same machine, which is out of scope for now. Flag this
to customers whose DCs reuse the same certificate for multiple purposes.

## Remaining unverified assumptions - must be lab-validated

1. Whether the LDAPS listener picks up a newly-written NTDS-store certificate immediately, only
   after `Restart-Service NTDS`, or only after a reboot. (Note: this is about **Add**, i.e. how fast
   a *new* certificate becomes active - separate from the Remove-behavior question resolved above,
   which was about what it takes to make LDAPS stop using an *old* one.)
2. Whether AD DS's own built-in automatic Personal-store certificate detection could later overwrite
   what WinLDAP wrote directly to the NTDS store, since the certificate also remains staged in
   Personal.
3. ~~The operational effect of removing the certificate currently active on port 636.~~ **Resolved
   above (2026-09-16)**: removing from the NTDS store alone (with or without an NTDS restart) is not
   sufficient; the Personal-store copy must also be removed, and no restart is needed once it is.
4. Whether the eligibility validator's rules (`Test-LdapsCertificateEligibility.ps1`) - Server-Auth
   EKU tolerance when absent, forest-root-domain SAN as an alternative to the DC's own FQDN - match
   real AD DS selection behavior closely enough to avoid false rejections.
5. **Active as of 2026-09-03** (see "Reversed on 2026-09-03" above): whether a JEA virtual
   account or gMSA has sufficient ACLs to write to
   `HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates`. This is the single
   most important open item now that remote/JEA support has been restored - it has not been
   lab-validated, and shipping WinLDAP's JEA support without validating it means a customer could
   configure a JEA endpoint that silently fails on this specific permission boundary. Note: the
   Personal-store removal added in this fix requires the same account to also have delete rights on
   `Cert:\LocalMachine\My` for the *removed* certificate's private key material - worth confirming
   alongside the NTDS-hive ACL check, not just assuming it follows the same permission level.

## Next step

Run `docs/winldap-module-validation.ps1`'s Step 4 (Remove) again against the rewritten
`Remove-KeyfactorLdapsCertificate`, and re-confirm via `ldp.exe`/`openssl s_client` from a separate
machine that LDAPS actually stops presenting the certificate immediately, with no restart - that
closes the loop on item 3 above using the *fixed* code, not just the raw registry mechanism that
diagnosed the problem. For item 5 (and the new Personal-store ACL question it raises), stand up a
real JEA endpoint per `docsource/content.md`'s setup steps (installing `Keyfactor.WinCert.LDAP`
alongside `Keyfactor.WinCert.Common`), then run `Get-KeyfactorDiagnostics` through it and the JEA
section of `docs/winldap-module-validation.ps1`. Update this file with findings once that's done.
