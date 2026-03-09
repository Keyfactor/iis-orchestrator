### Key Features and Representation

The IISU store type represents the IIS servers and their certificate bindings. It specifically caters to managing SSL/TLS certificates tied to IIS websites, allowing bind operations such as specifying site names, IP addresses, ports, and enabling Server Name Indication (SNI). By default, it supports job types like Inventory, Add, Remove, and Reenrollment, thereby offering comprehensive management capabilities for IIS certificates.

### Understanding SSL Flags

When binding certificates to IIS sites, the `sslFlags` property can be configured to modify the behavior of HTTPS bindings.  
These flags are **bitwise values**, meaning they can be combined by adding their numeric values together.

The available SSL flags depend on the version of Windows Server and IIS.

---

#### Windows Server 2016 (IIS 10.0)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 4 | Disable HTTP/2 |

---

#### Windows Server 2019 (IIS 10.0.17763)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 4 | Disable HTTP/2 |
| 8 | Disable OCSP Stapling |

---

#### Windows Server 2022 and later (IIS 10.0.20348+)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 4 | Disable HTTP/2 |
| 8 | Disable OCSP Stapling |
| 16 | Disable QUIC (HTTP/3) |
| 32 | Disable TLS 1.3 over TCP |
| 64 | Disable legacy TLS protocols |

---

#### Combining SSL Flags

Because `sslFlags` is a bitwise field, multiple options can be enabled by **adding their values together**.

**Example:**  
To enable SNI and disable HTTP/2:

The resulting SSL Flags value would be **5**.

---

#### ⚠️ Important Behavior Notes

When modifying SSL flags programmatically, **existing flag values must be preserved and combined correctly**.  
Changing SSL flags—especially SNI—without retaining the original binding configuration can lead to unintended behavior, including:

- HTTPS bindings being recreated
- Certificates appearing to be removed or reassigned
- Certificates being shared across bindings unexpectedly

This behavior occurs because SNI affects how IIS and HTTP.sys uniquely identify HTTPS bindings.  
Always update SSL flags using **bitwise operations** rather than overwriting the value.

For authoritative guidance on SSL bindings and the `sslFlags` property, refer to Microsoft documentation:

- IIS `<binding>` element (`sslFlags` attribute):  
  <https://learn.microsoft.com/iis/configuration/system.applicationhost/sites/site/bindings/binding>

- IIS SSL bindings and HTTP.sys behavior:  
  <https://learn.microsoft.com/iis/manage/configuring-security/how-to-set-up-ssl-on-iis>

---

#### Notes on Centralized Certificate Store (CCS)

**SSL Flag 2 (Centralized Certificate Store)** is currently **not supported** by this implementation.  
Using this flag will result in an error and the job will not complete successfully.

### Limitations and Areas of Confusion

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.
<br><br>When performing <b>Inventory</b>, all bound certificates <i>regardless</i> to their store location will be returned.
<br><br>When executing an Add or Renew Management job, the Store Location will be considered and place the certificate in that location.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.

- **Custom Alias and Private Keys:** The store type does not support custom aliases for individual entries and requires private keys because IIS certificates without private keys would be invalid.

## Overview

TODO Overview is a required section

