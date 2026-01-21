## Overview

The IIS Bound Certificate Store Type, identified by its short name 'IISU,' is designed for the management of certificates bound to IIS (Internet Information Services) servers. This store type allows users to automate and streamline the process of adding, removing, and reenrolling certificates for IIS sites, making it significantly easier to manage web server certificates.

### Key Features and Representation

The IISU store type represents the IIS servers and their certificate bindings. It specifically caters to managing SSL/TLS certificates tied to IIS websites, allowing bind operations such as specifying site names, IP addresses, ports, and enabling Server Name Indication (SNI). By default, it supports job types like Inventory, Add, Remove, and Reenrollment, thereby offering comprehensive management capabilities for IIS certificates.

### Understanding SSL Flags

When binding certificates to IIS sites, certain SSL flags can be configured to modify the behavior of the SSL bindings. Depending on what version of Windows Server and IIS, these are the following flags are available:

#### Windows Server 2016 (IIS 10.0):  #### Windows Server 2016 (IIS 10.0)

  * 0    No SNI
  * 1    Use SNI
  * 4    Disable HTTP/2.

#### Windows Server 2019 (IIS 10.0.17763)

  * 0    No SNI
  * 1    Use SNI
  * 4    Disable HTTP/2.
  * 8    Disable OCSP Stapling.

#### Windows Server 2022+ (IIS 10.0.20348+)

  * 0    No SNI
  * 1    Use SNI
  * 4    Disable HTTP/2.
  * 8    Disable OCSP Stapling.
  * 16    Disable QUIC.
  * 32    Disable TLS 1.3 over TCP.
  * 64    Disable Legacy TLS.

The SSL bitwise flags can be combined by adding their values together. For example, to enable SNI and disable HTTP/2, you would set the SSL Flags to 5 (1 + 4).

<b>Note:</B> SSL Flag 2 - Centralized Certificate Storage, is currently not supported.<br>
Using this flag will result in an error message and the job not being completed successfully.

### Limitations and Areas of Confusion

* **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.
<br><br>When performing <b>Inventory</b>, all bound certificates <i>regardless</i> to their store location will be returned.
<br><br>When executing an Add or Renew Management job, the Store Location will be considered and place the certificate in that location.

* **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.

* **Custom Alias and Private Keys:** The store type does not support custom aliases for individual entries and requires private keys because IIS certificates without private keys would be invalid.
