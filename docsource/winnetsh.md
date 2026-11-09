## Overview

WinNetSH is a store type for managing SSL certificates bound to HTTP.sys via `netsh http sslcert` - the binding mechanism used by services that sit directly on top of HTTP.sys without their own certificate-binding UI or cmdlet, most commonly the WinRM HTTPS listener, but also any other HTTP.sys-hosted service configured this way. It supports Inventory, Add, Remove, and Reenrollment (On-Device Key Generation / ODKG) of certificates, matching the job types supported by `WinCert`, `IISU`, `WinSql`, and `WinLDAP`.

There is no native PowerShell cmdlet for HTTP.sys SSL certificate bindings, so this store type shells out to `netsh.exe` and parses its text output, rather than using a `Cert:`/registry-based or managed-API approach the way the other store types do.

* NOTE: A binding is identified by its `IP:Port` (or `Hostname:Port`, for an SNI binding) - there is no "site" concept the way there is for WinIIS. Inventory is scoped to bindings whose certificate store (`certstorename`) matches the Certificate Store's configured Store Path; a binding pointing at a different store is not returned.
* NOTE: `netsh http add sslcert` requires an `AppId` (an arbitrary GUID identifying the owning application) but has no "update" verb, so renewing a certificate is implemented as delete-then-add against the same binding key. When the `AppId` entry parameter is left blank, this store type resolves one automatically: a renewal of an existing binding reuses that binding's current `AppId` (so it doesn't silently change), and a brand-new binding is given a freshly generated GUID. Either way, the `AppId` actually used is reported back in the job result message and in Inventory's `AppId` parameter - check there if an explicit `AppId` matters for the consuming service (some services validate their own `AppId` at startup).
* NOTE: Unlike WinIIS, a binding change here takes effect immediately - `netsh http sslcert` has no associated service to restart.
* NOTE: Remove removes both the `netsh http sslcert` binding and the certificate from the underlying Windows certificate store, but only removes the certificate if no other `sslcert` binding on the same machine still references it - the same "still in use elsewhere" check WinIIS performs before deleting a certificate.
* NOTE: The exact text format of `netsh http show sslcert` has only been verified against one Windows version as of this writing (see `docs/winnetsh-implementation-notes.md`). Add/Remove/Inventory have been round-tripped successfully against real bindings on that machine, but JEA support specifically has not yet been lab-validated - confirm the JEA run-as account has sufficient rights to run `netsh http add/delete sslcert` before relying on this over a JEA endpoint in production. If Inventory or binding operations behave unexpectedly on a different OS version, run `netsh http show sslcert` directly on the target server and compare its output shape against what `Keyfactor.WinCert.NetSH`'s parser expects.

## Requirements

WinNetSH supports both connection models used elsewhere in this extension:

* **Local agent**, using the `|LocalMachine` Client Machine naming convention (see [Client Machine Instructions](#note-regarding-client-machine)) - the orchestrator runs directly on the target server.
* **Remote WinRM** (optionally through a JEA endpoint), or **SSH** (when the orchestrator itself runs in a Linux container/host) - connecting to the target server from a centrally installed orchestrator, following the same `WinRM Protocol`/`WinRM Port`/`JEA Endpoint Name` configuration used by the other store types. See the **Just Enough Administration (JEA) Setup and Configuration** section in the main README; install the `Keyfactor.WinCert.NetSH` module (in addition to `Keyfactor.WinCert.Common`) on the target server to use JEA with WinNetSH.

Binding and unbinding `netsh http sslcert` entries requires local Administrator rights on the target server, the same as WinIIS - see **Security and Permission Considerations** in the main README.

## Certificate Store Configuration

When creating a Certificate Store for WinNetSH, the Store Path identifies the Windows certificate store (under `Cert:\LocalMachine`) that holds the bound certificates - typically `My` (the Personal store), matching netsh's `certstorename` parameter. The Client Machine value is either the target server's hostname/IP (for remote WinRM/SSH) or `<hostname>|LocalMachine` (for a local agent).

Each binding is described by the `IPAddress`, `Port`, optional `HostName` (for an SNI binding), and optional `AppId` entry parameters - see the AppId note above for how `AppId` is resolved when left blank.
