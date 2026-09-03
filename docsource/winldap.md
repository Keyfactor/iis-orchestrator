## Overview

WinLDAP is a store type designed for managing the AD DS (Active Directory Domain Services) LDAPS server certificate on a Domain Controller. It automates the certificate renewal workflow administrators traditionally perform by hand: importing the new certificate into the Domain Controller's Personal ("My") certificate store, then registering it into the NTDS-service-specific certificate store (registry-backed at `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates`) that the LDAPS listener (port 636) reads from.

Writing into that registry-backed store uses only the built-in .NET certificate APIs and ordinary registry access already available on every Domain Controller - no `certutil.exe` calls, and no new executables or DLLs are installed on the DC to do it.

* NOTE: Each Domain Controller is managed as its own independent Certificate Store, since every DC's LDAPS certificate has a unique Subject/SAN matching that DC's own FQDN. WinLDAP does not fan a single certificate out to multiple DCs the way WinAdfs fans a shared certificate out to ADFS farm nodes.
* NOTE: Inventory and Remove are scoped strictly to the NTDS service store, which is treated as the single source of truth. The Personal-store copy created during Add is an internal staging detail and is not separately visible in Inventory, nor removed by Remove.
* NOTE: How quickly the LDAPS listener picks up a newly written certificate, and what happens on port 636 if the currently active certificate is removed, have not yet been validated against a live Domain Controller. Treat this store type as pre-production until that validation is complete.

## Requirements

**This release is local-agent-only.** The Universal Orchestrator must run as a local agent installed directly on the Domain Controller, using the `|LocalMachine` Client Machine naming convention (see [Client Machine Instructions](#note-regarding-client-machine)) - remote WinRM/JEA connections are not supported yet.

This is a narrower restriction than the technical design requires - each Domain Controller is managed independently with no fan-out to other nodes, so there is no WinRM double-hop concern the way there is for `WinAdfs`. The restriction instead reflects two things specific to Domain Controllers as Tier-0 assets:

* Many hardened Active Directory environments disable inbound WinRM to DCs as a blanket policy, regardless of payload, so remote management may not be usable there anyway.
* Whether a JEA virtual account's permissions are sufficient to write to `HKLM\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates` has not been validated on a real hardened DC. Rather than ship and support a remote/JEA path that may silently fail on that permission boundary, this release requires the orchestrator to run as a local agent - the same account the Universal Orchestrator service itself runs as, which needs write access to that registry hive.

Remote WinRM/JEA support may be added in a future release once that permission question has been validated in a lab environment. Until then, configuring a WinLDAP store with a remote Client Machine value is not supported.

## Certificate Store Configuration

When creating a Certificate Store for WinLDAP, the Store Path is fixed to `NTDS\My`, identifying the NTDS service certificate store rather than the ordinary Personal store. The Client Machine value must use the `<hostname>|LocalMachine` convention.
