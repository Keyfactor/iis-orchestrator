## Overview

WinLDAP is a store type designed for managing the AD DS (Active Directory Domain Services) LDAPS server certificate on a Domain Controller. It automates the certificate renewal workflow administrators traditionally perform by hand: importing the new certificate into the Domain Controller's Personal ("My") certificate store, then registering it into the NTDS-service-specific certificate store (registry-backed at `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates`) that the LDAPS listener (port 636) reads from.

* NOTE: Each Domain Controller is managed as its own independent Certificate Store, since every DC's LDAPS certificate has a unique Subject/SAN matching that DC's own FQDN. WinLDAP does not fan a single certificate out to multiple DCs the way WinAdfs fans a shared certificate out to ADFS farm nodes.
* NOTE: Inventory and Remove are scoped strictly to the NTDS service store, which is treated as the single source of truth. The Personal-store copy created during Add is an internal staging detail and is not separately visible in Inventory, nor removed by Remove.
* NOTE: Several implementation details of this store type - the exact `certutil` syntax used to read/write the NTDS service store, the permissions required to write that registry hive, and how quickly the LDAPS listener picks up a new certificate - have not yet been validated against a live Domain Controller. Treat this store type as pre-production until that validation is complete.

## Requirements

Unlike WinAdfs, WinLDAP does **not** require the Universal Orchestrator to run as a local agent installed directly on the Domain Controller. Because each Domain Controller is managed independently (no fan-out to other nodes), every operation this store type performs touches only the one machine it is already connected to - there is no second network hop, and therefore no WinRM double-hop concern. You may choose either:

* **Local agent**, using the `|LocalMachine` Client Machine naming convention (see [Client Machine Instructions](#note-regarding-client-machine)), or
* **Remote WinRM**, connecting to the Domain Controller from a centrally installed orchestrator, optionally through a JEA endpoint.

Domain Controllers are Tier-0 assets, and many hardened Active Directory environments disable inbound WinRM to DCs as a blanket policy regardless of payload. That is a customer security-posture decision to make independently of this store type's own technical support for remote management.

## Certificate Store Configuration

When creating a Certificate Store for WinLDAP, the Store Path is fixed to `NTDS\My`, identifying the NTDS service certificate store rather than the ordinary Personal store. The Client Machine value is either the Domain Controller's hostname (for remote WinRM) or `<hostname>|LocalMachine` (for a local agent).
