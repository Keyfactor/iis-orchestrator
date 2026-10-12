# WinNetSH Store Type — Implementation Notes

Working notes for the WinNetSH store type (`netsh http sslcert` binding management), covering why it
was built this way, exactly what changed, and what still needs to be validated on a real Windows
server - see `docs/winnetsh-module-validation.ps1` for the hands-on validation script referenced
below.

## Why

Nothing in this extension manages raw HTTP.sys SSL certificate bindings (`netsh http add/delete
sslcert`) - the mechanism used by services that sit directly on HTTP.sys without their own binding
UI or cmdlet, most commonly the WinRM HTTPS listener. WinNetSH adds that as a new store type,
following the same architecture as `WinIIS`/`WinSQL`/`WinLDAP` in this repo.

## Design decisions

- **Binding-based, not fixed-store-based** - modeled on `WinIIS` (an `IPAddress`/`Port`/`HostName`
  binding identity built from entry parameters, with rebind-on-renewal logic) rather than `WinLDAP`
  (one fixed store per Certificate Store). There's no "site" concept, so the alias is simpler than
  IIS's: `Thumbprint:IPAddress:Port[:HostName]`, with no `SiteName` segment.
- **No native cmdlet exists** for HTTP.sys SSL bindings, so `Keyfactor.WinCert.NetSH` shells out to
  `netsh.exe` and parses its text output (`ConvertFrom-NetShSslCertOutput.ps1`) - this is the one
  genuinely new mechanism in this store type; everything else (session handling, PAM resolution,
  `New-KeyfactorResult`/`ResultObject` conventions, ODKG flow) reuses the existing shared code as-is.
- **AppId resolution** - `netsh http add sslcert` requires an `AppId` GUID but there's no way for
  Keyfactor Command to know what a good one is, and `netsh` has no "update" verb (rebinding is
  delete-then-add). Rather than requiring the user to supply one on every store, or silently minting
  a new one on every renewal (which would change the AppId a consuming service might rely on),
  `Set-NetShSslCertBinding.ps1` resolves it as: caller-supplied value if given; else the existing
  binding's own AppId if one is found at that key (preserves it across a renewal); else a freshly
  generated GUID for a genuinely new binding. The resolved value and which of the three cases
  produced it (`Supplied`/`Reused`/`Generated`) are always returned in the PowerShell result's
  `Details`, and surfaced back to Command in the job result message and in Inventory's `AppId`
  parameter whenever it wasn't the caller-supplied value.
- **Rebind-then-cleanup on Add, mirroring WinIIS**: stage the certificate via the shared
  `Add-KeyfactorCertificate`, bind it (delete-then-add against the same `ipport`/`hostnameport`),
  then - only if this Add is a renewal (the outgoing `JobCertificate.Alias` carried an old
  thumbprint) - attempt to remove the superseded certificate from the store if no other binding
  still references it, downgrading a cleanup failure to a `Warning` rather than failing the whole job.
- **JEA supported** - costs only a new `.psrc` (`Keyfactor.WinCert.NetSH.psrc`) listing
  `C:\Windows\System32\netsh.exe` in `VisibleExternalCommands`; no changes to the shared session
  configuration (`KeyfactorWinCert.pssc`) beyond documentation/example updates.
- **Add/Remove/Reenrollment (ODKG) supported**, matching `WinIIS`/`WinSQL`'s scope rather than
  `WinLDAP`'s Add/Remove-only initial release, since ODKG support was an explicit requirement.

## What changed

**New:**
- `IISU/ImplementedStoreTypes/WinNetSH/Inventory.cs`, `Management.cs`, `ReEnrollment.cs`,
  `WinNetSHBinding.cs`, `NetSHBindingInfo.cs`, `NetSHCertificateInfo.cs` - C# job classes, mirroring
  the `WinIIS` pattern (`PSHelper` construction, PS function dispatch, `ResultObject` parsing,
  bind/unbind helper, alias parsing).
- `IISU/PowerShell/Keyfactor.WinCert.NetSH/` - new PowerShell module:
  - `Public/Get-KeyfactorNetSHBoundCertificates.ps1`, `New-KeyfactorNetSHBinding.ps1`,
    `Remove-KeyfactorNetSHBinding.ps1`, `Remove-KeyfactorNetSHCertificateIfUnused.ps1`
  - `Private/ConvertFrom-NetShSslCertOutput.ps1`, `Get-NetShSslCertBinding.ps1`,
    `Set-NetShSslCertBinding.ps1` - the netsh-output-parsing and delete-then-add/AppId-resolution
    logic (see "Remaining unverified assumptions" below)
  - `RoleCapabilities/Keyfactor.WinCert.NetSH.psrc` - JEA role capability, mirroring
    `Keyfactor.WinCert.IIS.psrc` (needs `VisibleExternalCommands` for `netsh.exe`, unlike WinLDAP's
    registry-only approach)
  - `Keyfactor.WinCert.NetSH.psm1` - module loader/exports
- `docsource/winnetsh.md` - short store-type overview (stitched into the generated `README.md`)
- `docs/winnetsh-implementation-notes.md` (this file) and `docs/winnetsh-module-validation.ps1`
- `WindowsCertStore.UnitTests/NetSHBindingInfoTests.cs` - xUnit coverage for `NetSHBindingInfo`'s
  alias parsing and entry-parameter-dictionary construction (pure C# logic, no PowerShell/network
  dependency) - the first automated test coverage any PowerShell-script-backed store type in this
  repo has had; every other one relies solely on manual validation scripts.

**Modified:**
- `integration-manifest.json` - new `WinNetSH` store type entry (Add/Remove/Enrollment,
  `StorePathValue: "My"`, `PrivateKeyAllowed: "Required"`, `BlueprintAllowed: true`, entry parameters
  `IPAddress`/`Port`/`HostName`/`AppId`/`ProviderName`, no `RestartService` property since netsh
  binding changes take effect immediately)
- `IISU/manifest.json` - `CertStores.WinNetSH.Inventory`/`.Management`/`.ReEnrollment` type-mapping
  entries
- `IISU/WinCertJobTypeBase.cs` - added `WinNetSH` to `CertStoreBindingTypeENUM`
- `IISU/ClientPSCertStoreReEnrollment.cs` - new `case CertStoreBindingTypeENUM.WinNetSH:` block,
  and `adminPrivilegesRequired` now also true for WinNetSH (binding requires local Administrator
  rights, like WinIIS)
- `IISU/WindowsCertStore.csproj` - copy-to-output and folder-include entries for the new PowerShell
  module folder
- `IISU/PowerShell/Build/KeyfactorWinCert.pssc` - documentation/example updates only (module install
  snippet, `RoleDefinitions` example table, available-RoleCapabilities comment list)
- `docsource/content.md` - module table, JEA install snippet, `RoleDefinitions` example table,
  store-type bullet list, and permissions bullet list updated for WinNetSH

**Explicitly not modified**: `IISU/PSHelper.cs`, `IISU/Models/JobProperties.cs` - no new shared
plumbing was needed.

## Status as of this writing

- Solution builds clean (0 errors) on `net8.0`/`net10.0`.
- All existing unit tests pass except one pre-existing, unrelated failure
  (`AdfsUnitTests.Test_AdfsInventory`) - also documented as a known, pre-existing issue in
  `docs/winldap-implementation-notes.md`, not a regression introduced here.
- New `NetSHBindingInfoTests` (alias parsing, dictionary construction, missing-key error paths) pass.
- **Local-agent path validated live on a real Windows 11 development machine** (elevated, non-JEA) -
  see "Resolved during local validation (2026-09-21)" below. **JEA has not been validated** - the
  `.psrc`'s open question about whether a JEA run-as identity has sufficient rights to run `netsh
  http add/delete sslcert` remains unverified; run the "JEA VARIANT" section of
  `docs/winnetsh-module-validation.ps1` against a real JEA endpoint before relying on this in
  production over JEA specifically.

## Resolved during local validation (2026-09-21)

- **`ConvertFrom-NetShSslCertOutput.ps1` parsed real `netsh http show sslcert` output correctly**,
  including all 101 pre-existing SSL bindings on the test machine, both `IP:port` and (once one was
  created for the test) `Hostname:port`/SNI entries, and the `Extended Property:` sub-blocks netsh
  emits per binding (interleaved with no blank-line separator from the main block) did not corrupt
  parsing of the fields this store type actually reads (`IP:port`/`Hostname:port`, `Certificate
  Hash`, `Application ID`, `Certificate Store Name`) - those four labels always had 2+ spaces of
  padding before their separating colon in every binding observed, even though some `Extended
  Property` sub-fields (e.g. `Max Concurrent Client Streams: 100`) did not and were correctly
  filtered out as a result (harmless, since those fields aren't extracted).
- **Found and fixed a real bug**: `ConvertFrom-NetShSslCertOutput`'s `-RawOutput` parameter was
  declared `[Parameter(Mandatory = $true)] [AllowEmptyCollection()] [string[]]`. `netsh`'s output
  legitimately contains blank-line array elements (used as block separators). PowerShell's mandatory-
  parameter validation rejects the *entire array* if *any* element is an empty string -
  `[AllowEmptyCollection()]` only permits the collection itself to have zero elements, not empty
  string elements within it. Every call failed with "Cannot bind argument to parameter 'RawOutput'
  because it is an empty string" until `[AllowEmptyString()]` was added alongside it. This would have
  broken Inventory (and everything else that calls `Get-NetShSslCertBinding`) unconditionally, on
  every machine, regardless of OS version - not a lab-environment-specific issue.
- **AppId resolution verified for all three cases** against real `netsh http add/delete sslcert`
  calls: a brand-new binding got a freshly generated GUID (`Generated`); rebinding the same `ipport`
  with a new certificate and no `-AppId` reused the previous binding's own AppId exactly (`Reused`);
  supplying `-AppId` explicitly overrode both (`Supplied`). Confirmed via `netsh http show sslcert`
  after each step, not just via the function's own return value.
- **`Certificate Store Name` case varies per binding** even on the same machine (`My` vs `MY` were
  both observed among the 101 real bindings, presumably reflecting whatever casing the tool that
  created each one used) - `Get-KeyfactorNetSHBoundCertificates`'s `-eq` comparison against
  `-StoreName` is case-insensitive by default in PowerShell, so this was already handled correctly,
  but it's worth calling out since a case-sensitive rewrite would silently under-report Inventory.
- **Full round-trip confirmed with zero side effects on the other 101 real bindings**: create (new
  `ipport` binding, AppId generated) -> rebind (renewal, AppId reused) -> explicit-AppId rebind ->
  SNI/`hostnameport` binding created and parsed correctly -> both bindings removed (`Remove-
  KeyfactorNetSHBinding`, including a Skipped/idempotent second remove) -> both test certificates
  removed via `Remove-KeyfactorNetSHCertificateIfUnused` -> final `netsh http show sslcert` count
  back to exactly 101, matching the pre-test baseline.

## Remaining unverified assumptions - must still be lab-validated

- **JEA virtual-account/gMSA rights** - whether a JEA run-as identity with local-Administrator-
  equivalent rights is actually sufficient to run `netsh http add/delete sslcert` (as opposed to only
  the directly-elevated interactive session used above) has not been validated, mirroring the same
  open question WinLDAP's `.psrc` flags for its own registry writes. This is the main remaining gap.
- **`netsh http show sslcert` text format on other OS versions/locales** - validated above only
  against one Windows 11 machine's `netsh.exe`. A different Windows Server version or non-English
  locale could use different label text or padding; re-run the parser against `netsh http show
  sslcert` output on every OS version this store type is actually deployed to, and compare label
  text if Inventory unexpectedly returns fewer items than `netsh` shows directly.
- **Exit-code/error-text behavior of `netsh http show sslcert ipport=<key>`** when no binding exists
  at that key - confirmed working (returns an empty result, not an error) on the test machine, but
  the specific exit code/message text this relies on has not been checked across OS versions.
- **IPv6 binding keys** - `Get-KeyfactorNetSHBoundCertificates.ps1` splits a binding key on the last
  colon to separate the address/hostname from the port (to tolerate IPv6 addresses like `[::]:443`,
  which contain colons of their own), but this has only been checked against IPv4 and hostname
  examples above, not against a real IPv6 binding.

## Next step

Run the "JEA VARIANT" section of `docs/winnetsh-module-validation.ps1` against a lab Windows Server
with a registered JEA endpoint (`Keyfactor.WinCert.Common` + `Keyfactor.WinCert.NetSH`), to close the
one open question local validation couldn't answer: JEA run-as account rights. Record results as a
dated entry in this file, following the convention above.
