## Overview

WinLDAP is a store type designed for managing the AD DS (Active Directory Domain Services) LDAPS server certificate on a Domain Controller. It automates the certificate renewal workflow administrators traditionally perform by hand: importing the new certificate into the Domain Controller's Personal ("My") certificate store, then registering it into the NTDS-service-specific certificate store (registry-backed at `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates`) that the LDAPS listener (port 636) reads from.

Writing into that registry-backed store uses only the built-in .NET certificate APIs and ordinary registry access already available on every Domain Controller - no `certutil.exe` calls, and no new executables or DLLs are installed on the DC to do it.

* NOTE: Each Domain Controller is managed as its own independent Certificate Store, since every DC's LDAPS certificate has a unique Subject/SAN matching that DC's own FQDN. WinLDAP does not fan a single certificate out to multiple DCs the way WinAdfs fans a shared certificate out to ADFS farm nodes.
* NOTE: Inventory and Remove are scoped strictly to the NTDS service store, which is treated as the single source of truth. The Personal-store copy created during Add is an internal staging detail and is not separately visible in Inventory, nor removed by Remove.
* NOTE: How quickly the LDAPS listener picks up a newly written certificate, and what happens on port 636 if the currently active certificate is removed, have not yet been validated against a live Domain Controller. Treat this store type as pre-production until that validation is complete.

## Requirements

WinLDAP supports both connection models used elsewhere in this extension:

* **Local agent**, using the `|LocalMachine` Client Machine naming convention (see [Client Machine Instructions](#note-regarding-client-machine)) - the orchestrator runs directly on the Domain Controller and accesses the registry/certificate stores in-process.
* **Remote WinRM** (optionally through a JEA endpoint), or **SSH** (when the orchestrator itself runs in a Linux container/host) - connecting to the Domain Controller from a centrally installed orchestrator, following the same `WinRM Protocol`/`WinRM Port`/`JEA Endpoint Name` configuration used by `WinSQL` and the other store types. See the **Just Enough Administration (JEA) Setup and Configuration** section in the main README for the general setup walkthrough; install the `Keyfactor.WinCert.LDAP` module (in addition to `Keyfactor.WinCert.Common`) on the Domain Controller to use JEA with WinLDAP.

Each Domain Controller is managed independently with no fan-out to other nodes (unlike `WinAdfs`'s farm model), so there is no WinRM double-hop concern - every operation this store type performs touches only the one DC already connected to.

**Before relying on remote WinRM/JEA for WinLDAP in production, validate the following against your own environment** - these are Domain-Controller-specific considerations that don't apply to this extension's other store types:

* Domain Controllers are Tier-0 assets, and many hardened Active Directory environments disable inbound WinRM to DCs as a blanket policy regardless of payload. Confirm with your AD/security team whether remote management is even permitted before configuring it.
* Whether a JEA virtual account or gMSA has sufficient rights to write to `HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates` (the registry-backed store the LDAPS listener reads from) has not been lab-validated by Keyfactor as of this writing. Run `Get-KeyfactorDiagnostics` through the JEA session and perform a full Add/Remove round-trip against a disposable test certificate on a lab Domain Controller first (see `docs/winldap-ntds-validation.ps1` and `docs/winldap-module-validation.ps1` in the repository).

## Certificate Store Configuration

When creating a Certificate Store for WinLDAP, the Store Path is fixed to `NTDS\My`, identifying the NTDS service certificate store rather than the ordinary Personal store. The Client Machine value is either the Domain Controller's hostname/IP (for remote WinRM/SSH) or `<hostname>|LocalMachine` (for a local agent).
