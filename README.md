<h1 align="center" style="border-bottom: none">
    Windows Certificate Universal Orchestrator Extension
</h1>

<p align="center">
  <!-- Badges -->
<img src="https://img.shields.io/badge/integration_status-production-3D1973?style=flat-square" alt="Integration Status: production" />
<a href="https://github.com/Keyfactor/iis-orchestrator/releases"><img src="https://img.shields.io/github/v/release/Keyfactor/iis-orchestrator?style=flat-square" alt="Release" /></a>
<img src="https://img.shields.io/github/issues/Keyfactor/iis-orchestrator?style=flat-square" alt="Issues" />
<img src="https://img.shields.io/github/downloads/Keyfactor/iis-orchestrator/total?style=flat-square&label=downloads&color=28B905" alt="GitHub Downloads (all assets, all releases)" />
</p>

<p align="center">
  <!-- TOC -->
  <a href="#support">
    <b>Support</b>
  </a>
  ·
  <a href="#installation">
    <b>Installation</b>
  </a>
  ·
  <a href="#license">
    <b>License</b>
  </a>
  ·
  <a href="https://github.com/orgs/Keyfactor/repositories?q=orchestrator">
    <b>Related Integrations</b>
  </a>
</p>

## Overview

The Windows Certificate Orchestrator Extension is a multi-purpose integration that can remotely manage certificates on a Windows Server's Local Machine Store.  This extension currently manages certificates for the current store types:
* WinADFS - Rotates the Service-Communications certificate on the primary and secondary ADFS nodes
* WinCert - Certificates defined by path set for the Certificate Store
* WinIIS - IIS Bound certificates 
* WinSQL - Certificates that are bound to the specified SQL Instances

By default, most certificates are stored in the “Personal” (My) and “Web Hosting” (WebHosting) stores.
For a complete list of local machine cert stores you can execute the PowerShell command:

	Get-ChildItem Cert:\LocalMachine

The returned list will contain the actual certificate store name to be used when entering store location.

The ADFS extension performs both Inventory and Management Add jobs.  The other extensions implements four job types:  Inventory, Management Add/Remove, and Reenrollment.

The Keyfactor Universal Orchestrator (UO) and WinCert Extension can be installed on either Windows or Linux operating systems.  A UO service managing certificates on remote servers is considered to be acting as an Orchestrator, while a UO Service managing local certificates on the same server running the service is considered an Agent.  When acting as an Orchestrator, connectivity from the orchestrator server hosting the WinCert extension to the orchestrated server hosting the certificate stores(s) being managed is achieved via either an SSH (for Linux orchestrated servers) or WinRM (for Windows orchestrated servers) connection.  When acting as an agent (Windows only), WinRM may still be used, OR the certificate store can be configured to bypass a WinRM connection and instead directly access the orchestrator server's certificate stores.

![](images/orchestrator-agent.png)

Please refer to the READMEs for each supported store type for more information on proper configuration and setup for these different stores.  The supported configurations of Universal Orchestrator hosts and managed orchestrated servers are detailed below:

| | UO Installed on Windows | UO Installed on Linux |
|-----|-----|------|
|Orchestrated Server hosting certificate store(s) on remote Windows server|WinRM connection | SSH connection |
|Certificate store(s) on same server as orchestrator service (Agent)| WinRM connection or local file system | Not Supported |  

WinRM is used to remotely manage the certificate stores and IIS bindings on Windows machines only.  WinRM must be properly configured to allow the orchestrator on the server to manage the certificates.  Setting up WinRM is not in the scope of this document.

**Note:**
In version 2.0 of the IIS Orchestrator, the certificate store type has been renamed and additional parameters have been added. Prior to 2.0 the certificate store type was called “IISBin” and as of 2.0 it is called “IISU”. If you have existing certificate stores of type “IISBin”, you have three options:
1. Leave them as is and continue to manage them with a pre 2.0 IIS Orchestrator Extension. Create the new IISU certificate store type and create any new IIS stores using the new type.
1. Delete existing IIS stores. Delete the IISBin store type. Create the new IISU store type. Recreate the IIS stores using the new IISU store type.
1. Convert existing IISBin certificate stores to IISU certificate stores. There is not currently a way to do this via the Keyfactor API, so direct updates to the underlying Keyfactor SQL database is required. A SQL script (IIS-Conversion.sql) is available in the repository to do this. Hosted customers, which do not have access to the underlying database, will need to work Keyfactor support to run the conversion. On-premises customers can run the script themselves, but are strongly encouraged to ensure that a SQL backup is taken prior running the script (and also be confident that they have a tested database restoration process.)

**Note: There is an additional (and deprecated) certificate store type of “IIS” that ships with the Keyfactor platform. Migration of certificate stores from the “IIS” type to either the “IISBin” or “IISU” types is not currently supported.**

**Note: If Looking to use GMSA Accounts to run the Service Keyfactor Command 10.2 or greater is required for No Value checkbox to work**

The Windows Certificate Universal Orchestrator extension implements 4 Certificate Store Types. Depending on your use case, you may elect to use one, or all of these Certificate Store Types. Descriptions of each are provided below.

- [Windows Certificate](#WinCert)

- [IIS Bound Certificate](#IISU)

- [WinSql](#WinSql)

- [ADFS Rotation Manager](#WinAdfs)


## Compatibility

This integration is compatible with Keyfactor Universal Orchestrator version 10.1 and later.

## Support
The Windows Certificate Universal Orchestrator extension is supported by Keyfactor. If you require support for any issues or have feature request, please open a support ticket by either contacting your Keyfactor representative or via the Keyfactor Support Portal at https://support.keyfactor.com.

> If you want to contribute bug fixes or additional enhancements, use the **[Pull requests](../../pulls)** tab.

## Requirements & Prerequisites

Before installing the Windows Certificate Universal Orchestrator extension, we recommend that you install [kfutil](https://github.com/Keyfactor/kfutil). Kfutil is a command-line tool that simplifies the process of creating store types, installing extensions, and instantiating certificate stores in Keyfactor Command.


<details>
<summary><b>Using the WinCert Extension on Linux servers and/or with Docker Containers:</b></summary>

1. General SSH Setup Information: PowerShell 6 or higher and SSH must be installed on all computers.  Install SSH, including ssh server, that's appropriate for your platform.  You also need to install PowerShell from GitHub to get the SSH remoting feature.  The SSH server must be configured to create a SSH subsysten to host a PowerShell process on the remote computer.  It is suggested to turn off password authentication as this extension uses key-based authentication.  

2. SSH Authentication: When creating a Keyfactor certificate store for the WinCert orchestrator extension, the only protocol supported to communicate with Windows servers is ssh.  When providing the user id and password, the connection is attempted by creating a temporary private key file using the contents in the Password textbox. Therefore, the password field must contain the full SSH Private key.  

3. If you choose to run this extension in a containerized environment, the container image must include PowerShell version 7.5 or later, along with either OpenSSH clients (for SSH-based connections) or OpenSSL (if SSL/TLS operations are required). Additionally, the PWSMan PowerShell module must be installed to support management tasks and remote session functionality. These dependencies are required to ensure full compatibility when connecting from the container to remote Windows servers.  Below is an example Docker file snippet:
```
dnf install https://github.com/PowerShell/PowerShell/releases/download/v7.5.2/powershell-7.5.2-1.rh.x86_64.rpm
pwsh -Command 'Install-Module -Name PSWSMan'
dnf install openssh-clients openssl
```

</details>

<details>
<summary><b>Using the WinCert Extension on Windows servers:</b></summary>

1. When orchestrating management of external (and potentially local) certificate stores, the WinCert Orchestrator Extension makes use of WinRM to connect to external certificate store servers.  The security context used is the user id entered in the Keyfactor Command certificate store.  Make sure that WinRM is set up on the orchestrated server and that the WinRM port (by convention, 5985 for HTTP and 5986 for HTTPS) is part of the certificate store path when setting up your certificate stores jobs.  If running as an agent, managing local certificate stores, local commands are run under the security context of the user account running the Keyfactor Universal Orchestrator Service.

2. **JEA (Just Enough Administration) Support** — As a more secure alternative to granting the orchestrator service account full local administrator rights, the WinCert extension supports connecting via a JEA-enabled WinRM session endpoint. When JEA is configured, the orchestrator connects to a named PowerShell session configuration on the target server. Within that session, only the specific Keyfactor certificate management functions are exposed — no general PowerShell commands, no file system access, and no administrative cmdlets are available to the connecting account. This dramatically reduces the attack surface on managed servers and allows you to follow the principle of least privilege. JEA is configured on a per-certificate-store basis by entering the JEA endpoint name in the **JEA Endpoint Name** parameter when creating or editing a certificate store in Keyfactor Command. Refer to the **Just Enough Administration (JEA) Setup and Configuration** section below for complete step-by-step setup instructions.

3. **Important:** JEA cannot be used when the certificate store is configured to access the local machine directly (i.e., when the Client Machine value contains `|LocalMachine` or is set to `localhost`/`LocalMachine`). JEA requires an actual WinRM network connection to the target server. If a JEA Endpoint Name is configured and the store is also set to LocalMachine, the job will fail immediately with an ambiguous configuration error. To manage a local machine's certificates using JEA, set the Client Machine to the server's actual hostname or IP address and configure the JEA endpoint normally.

</details>

<details>
<summary><b>Just Enough Administration (JEA) Setup and Configuration:</b></summary>

### What is JEA?

Just Enough Administration (JEA) is a PowerShell security technology built into Windows that allows administrators to create constrained, audited remote PowerShell sessions. Instead of granting a service account full administrative access to a server, JEA lets you define exactly which PowerShell functions, cmdlets, and external commands are permitted within a remote session. The connecting account runs commands in that restricted environment — it cannot browse the file system, run arbitrary scripts, or invoke any command that has not been explicitly permitted.

JEA operates through two types of configuration files:

- **Session Configuration file (`.pssc`)** — Defines the overall session: the language mode, who is allowed to connect, which role capabilities to apply, whether to use a virtual run-as account or a Group Managed Service Account, and where to write audit transcripts. This file is registered with WinRM using `Register-PSSessionConfiguration` and becomes a named WinRM endpoint on the target server.

- **Role Capability files (`.psrc`)** — Defines the functions, cmdlets, and external commands that are visible within the session to users assigned that role. Each Keyfactor module ships with its own `.psrc` file that whitelists only the functions required for certificate management.

When the Keyfactor orchestrator connects to a JEA endpoint, it runs inside a `ConstrainedLanguage` PowerShell session backed by pre-installed, fully-trusted module code. The orchestrator can invoke Keyfactor certificate management functions, but nothing else. Every command executed in the session is recorded to a transcript file for audit purposes.

---

### Why Use JEA with the WinCert Extension?

The default WinRM connection model requires the orchestrator service account to have local administrator rights on every managed server. While functional, this violates the principle of least privilege and creates a broad attack surface — if the service account credentials were ever compromised, an attacker would have administrative access to every managed server. JEA addresses this by:

- **Limiting command exposure** — The remote session only exposes the specific Keyfactor functions needed. An attacker with the service account credentials cannot run arbitrary commands or explore the target server.
- **Running as a privileged virtual or managed service account** — The connecting account itself does not need administrative rights. The JEA session can run the actual commands under a local virtual account or a Group Managed Service Account (gMSA) that has only the rights needed to manage certificates.
- **Full audit trail** — Every JEA session is automatically transcribed to a log file on the target server. You have a complete record of every function called, with what parameters, and at what time.
- **Simplified permission management** — Rather than managing complex local administrator group membership across dozens of servers, you create a single AD group of orchestrator service accounts that are permitted to connect to the JEA endpoint.

---

### How JEA Works with the WinCert Extension

When the **JEA Endpoint Name** field is populated on a certificate store, the orchestrator changes its connection behavior:

1. It connects to the target server via WinRM using the configured credentials, but specifies the named JEA session configuration (`-ConfigurationName keyfactor.wincert`) instead of opening a standard administrative session.
2. The JEA session loads the pre-installed Keyfactor PowerShell modules from the target server's system module path (`C:\Program Files\WindowsPowerShell\Modules\`). Because these modules are installed in a trusted location, they run as fully trusted code and can use .NET APIs freely.
3. The orchestrator does **not** inject script content into the session. Instead, it calls the pre-loaded module functions by name, passing parameters. This is different from the standard WinRM mode, which loads scripts at session start.
4. A pre-flight check verifies that the Keyfactor modules are installed and accessible before any job runs. If the modules are not found, the job fails immediately with an actionable error message.
5. All commands executed during the session are written to a transcript in `C:\ProgramData\Keyfactor\JEA\Transcripts\` on the target server.

---

### Prerequisites

Before configuring JEA on a target server, ensure the following:

- **Windows PowerShell 5.1** is installed on the target server (included with Windows Server 2016 and later; available via Windows Management Framework 5.1 for Windows Server 2012 R2).
- **WinRM is enabled and configured** on the target server. Verify with: `Test-WSMan -ComputerName <target>`.
- **The Keyfactor orchestrator deployment package** has been extracted. The `PowerShell` folder within the extension contains the module directories and JEA configuration files.
- **Local Administrator access** on the target server is required to perform the one-time JEA setup (registering the session configuration and installing modules). This is a setup-time requirement only — once configured, the orchestrator service account does not need administrator rights.

---

### Keyfactor PowerShell Module Overview

The WinCert extension ships three PowerShell modules. Each module contains a `RoleCapabilities` subfolder with a `.psrc` file that defines which functions are visible in a JEA session.

| Module | Store Types Supported | Purpose |
|---|---|---|
| `Keyfactor.WinCert.Common` | WinCert, WinIIS, WinSQL | Certificate inventory, add, remove, and re-enrollment (CSR generation and signed cert import). Required for all store types. |
| `Keyfactor.WinCert.IIS` | WinIIS | IIS site binding management (get, create, remove bindings). |
| `Keyfactor.WinCert.SQL` | WinSQL | SQL Server certificate binding management (get, bind, unbind). |

Install only the modules needed for the store types you manage on that server. For example, a server that only hosts IIS certificates needs `Keyfactor.WinCert.Common` and `Keyfactor.WinCert.IIS`.

---

### Step-by-Step Setup Guide

#### Step 1: Locate the JEA Configuration Files

After deploying the Keyfactor Universal Orchestrator with the WinCert extension, navigate to the extension's output directory. You will find a `PowerShell` folder containing:

```
PowerShell\
  Keyfactor.WinCert.Common\       ← Module: common certificate operations
  Keyfactor.WinCert.IIS\          ← Module: IIS binding management
  Keyfactor.WinCert.SQL\          ← Module: SQL Server binding management
  Build\
    KeyfactorWinCert.pssc          ← JEA Session Configuration file
```

Copy this entire `PowerShell` folder to the target server (or to a network share accessible from the target server) to perform the setup steps below.

---

#### Step 2: Install the Keyfactor PowerShell Modules on the Target Server

On the **target server**, open an elevated PowerShell prompt (Run as Administrator) and run the following commands. Adjust the source path (`$sourcePath`) to wherever you placed the `PowerShell` folder in Step 1.

```powershell


## Certificate Store Types

To use the Windows Certificate Universal Orchestrator extension, you **must** create the Certificate Store Types required for your use-case. This only needs to happen _once_ per Keyfactor Command instance.

The Windows Certificate Universal Orchestrator extension implements 4 Certificate Store Types. Depending on your use case, you may elect to use one, or all of these Certificate Store Types.

### WinCert

<details><summary>Click to expand details</summary>


The Windows Certificate Certificate Store Type, known by its short name 'WinCert,' enables the management of certificates within the Windows local machine certificate stores. This store type is a versatile option for general Windows certificate management and supports functionalities including inventory, add, remove, and reenrollment of certificates.

The store type represents the various certificate stores present on a Windows Server. Users can specify these stores by entering the correct store path. To get a complete list of available certificate stores, the PowerShell command `Get-ChildItem Cert:\LocalMachine` can be executed, providing the actual certificate store names needed for configuration.

#### Key Features and Considerations

- **Functionality:** The WinCert store type supports essential certificate management tasks, such as inventorying existing certificates, adding new certificates, removing old ones, and reenrolling certificates.

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.




#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | 🔲 Unchecked  |
| Reenrollment | ✅ Checked |
| Create       | 🔲 Unchecked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand WinCert kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # Windows Certificate
   kfutil store-types create WinCert
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the WinCert store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual WinCert details</summary>

   Create a store type called `WinCert` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | Windows Certificate | Display name for the store type (may be customized) |
   | Short Name | WinCert | Short display name for the store type |
   | Capability | WinCert | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | 🔲 Unchecked |  Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | ✅ Checked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | 🔲 Unchecked |  Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![WinCert Basic Tab](docsource/images/WinCert-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![WinCert Advanced Tab](docsource/images/WinCert-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |
   | spnwithport | SPN With Port | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. | Bool | false | 🔲 Unchecked |
   | WinRM Protocol | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. | MultipleChoice | https,http,ssh | ✅ Checked |
   | WinRM Port | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. | String | 5986 | ✅ Checked |
   | ServerUsername | Server Username | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'.  (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerPassword | Server Password | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerUseSsl | Use SSL | Determine whether the server uses SSL or not (This field is automatically created) | Bool | true | ✅ Checked |

   The Custom Fields tab should look like this:

   ![WinCert Custom Fields Tab](docsource/images/WinCert-custom-fields-store-type-dialog.png)


   ###### SPN With Port
   Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.

   ![WinCert Custom Field - spnwithport](docsource/images/WinCert-custom-field-spnwithport-dialog.png)
   ![WinCert Custom Field - spnwithport](docsource/images/WinCert-custom-field-spnwithport-validation-options-dialog.png)



   ###### WinRM Protocol
   Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment.

   ![WinCert Custom Field - WinRM Protocol](docsource/images/WinCert-custom-field-WinRM Protocol-dialog.png)
   ![WinCert Custom Field - WinRM Protocol](docsource/images/WinCert-custom-field-WinRM Protocol-validation-options-dialog.png)



   ###### WinRM Port
   String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22.

   ![WinCert Custom Field - WinRM Port](docsource/images/WinCert-custom-field-WinRM Port-dialog.png)
   ![WinCert Custom Field - WinRM Port](docsource/images/WinCert-custom-field-WinRM Port-validation-options-dialog.png)



   ###### Server Username
   Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'.  (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Server Password
   Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Use SSL
   Determine whether the server uses SSL or not (This field is automatically created)

   ![WinCert Custom Field - ServerUseSsl](docsource/images/WinCert-custom-field-ServerUseSsl-dialog.png)
   ![WinCert Custom Field - ServerUseSsl](docsource/images/WinCert-custom-field-ServerUseSsl-validation-options-dialog.png)





   ##### Entry Parameters Tab

   | Name | Display Name | Description | Type | Default Value | Entry has a private key | Adding an entry | Removing an entry | Reenrolling an entry |
   | ---- | ------------ | ---- | ------------- | ----------------------- | ---------------- | ----------------- | ------------------- | ----------- |
   | ProviderName | Crypto Provider Name | Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers' | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |

   The Entry Parameters tab should look like this:

   ![WinCert Entry Parameters Tab](docsource/images/WinCert-entry-parameters-store-type-dialog.png)


   ##### Crypto Provider Name
   Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers'

   ![WinCert Entry Parameter - ProviderName](docsource/images/WinCert-entry-parameters-store-type-dialog-ProviderName.png)
   ![WinCert Entry Parameter - ProviderName](docsource/images/WinCert-entry-parameters-store-type-dialog-ProviderName-validation-options.png)



   </details>
</details>

### IISU

<details><summary>Click to expand details</summary>


#### Key Features and Representation

The IISU store type represents the IIS servers and their certificate bindings. It specifically caters to managing SSL/TLS certificates tied to IIS websites, allowing bind operations such as specifying site names, IP addresses, ports, and enabling Server Name Indication (SNI). By default, it supports job types like Inventory, Add, Remove, and Reenrollment, thereby offering comprehensive management capabilities for IIS certificates.

#### Understanding SSL Flags

When binding certificates to IIS sites, the `sslFlags` property can be configured to modify the behavior of HTTPS bindings.  
These flags are **bitwise values**, meaning they can be combined by adding their numeric values together.

The available SSL flags depend on the version of Windows Server and IIS.

Note that SNI/SSL Flags were introduced in IIS 8.0, so they are not available in Windows Server 2012 (IIS 8.0) and earlier versions, nor supported in this extension.

---

##### Windows Server 20162012 R2/Windows 8.1 (IIS 8.5)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 2 | Centralized Certificate Store (CCS) (Not Supported) |
| 4 | Disable HTTP/2 |

---

##### Windows Server 2016 (IIS 10.0)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 4 | Disable HTTP/2 |

---

##### Windows Server 2019 (IIS 10.0.17763)

| Value | Description |
|-----:|-------------|
| 0 | No SNI (traditional IP:Port binding) |
| 1 | Enable Server Name Indication (SNI) |
| 4 | Disable HTTP/2 |
| 8 | Disable OCSP Stapling |

---

##### Windows Server 2022 and later (IIS 10.0.20348+)

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

##### Combining SSL Flags

Because `sslFlags` is a bitwise field, multiple options can be enabled by **adding their values together**.

**Example:**  
To enable SNI and disable HTTP/2:

The resulting SSL Flags value would be **5**.

---

##### ⚠️ Important Behavior Notes

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

##### Notes on Centralized Certificate Store (CCS)

**SSL Flag 2 (Centralized Certificate Store)** is currently **not supported** by this implementation.  
Using this flag will result in an error and the job will not complete successfully.

#### Limitations and Areas of Confusion

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.
<br><br>When performing <b>Inventory</b>, all bound certificates <i>regardless</i> to their store location will be returned.
<br><br>When executing an Add or Renew Management job, the Store Location will be considered and place the certificate in that location.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.

- **Custom Alias and Private Keys:** The store type does not support custom aliases for individual entries and requires private keys because IIS certificates without private keys would be invalid.




#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | 🔲 Unchecked  |
| Reenrollment | ✅ Checked |
| Create       | 🔲 Unchecked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand IISU kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # IIS Bound Certificate
   kfutil store-types create IISU
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the IISU store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual IISU details</summary>

   Create a store type called `IISU` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | IIS Bound Certificate | Display name for the store type (may be customized) |
   | Short Name | IISU | Short display name for the store type |
   | Capability | IISU | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | 🔲 Unchecked |  Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | ✅ Checked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | 🔲 Unchecked |  Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | 🔲 Unchecked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![IISU Basic Tab](docsource/images/IISU-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Required | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![IISU Advanced Tab](docsource/images/IISU-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |
   | spnwithport | SPN With Port | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. | Bool | false | 🔲 Unchecked |
   | WinRM Protocol | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. | MultipleChoice | https,http,ssh | ✅ Checked |
   | WinRM Port | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. | String | 5986 | ✅ Checked |
   | ServerUsername | Server Username | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerPassword | Server Password | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerUseSsl | Use SSL | Determine whether the server uses SSL or not (This field is automatically created) | Bool | true | ✅ Checked |

   The Custom Fields tab should look like this:

   ![IISU Custom Fields Tab](docsource/images/IISU-custom-fields-store-type-dialog.png)


   ###### SPN With Port
   Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.

   ![IISU Custom Field - spnwithport](docsource/images/IISU-custom-field-spnwithport-dialog.png)
   ![IISU Custom Field - spnwithport](docsource/images/IISU-custom-field-spnwithport-validation-options-dialog.png)



   ###### WinRM Protocol
   Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment.

   ![IISU Custom Field - WinRM Protocol](docsource/images/IISU-custom-field-WinRM Protocol-dialog.png)
   ![IISU Custom Field - WinRM Protocol](docsource/images/IISU-custom-field-WinRM Protocol-validation-options-dialog.png)



   ###### WinRM Port
   String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22.

   ![IISU Custom Field - WinRM Port](docsource/images/IISU-custom-field-WinRM Port-dialog.png)
   ![IISU Custom Field - WinRM Port](docsource/images/IISU-custom-field-WinRM Port-validation-options-dialog.png)



   ###### Server Username
   Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Server Password
   Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Use SSL
   Determine whether the server uses SSL or not (This field is automatically created)

   ![IISU Custom Field - ServerUseSsl](docsource/images/IISU-custom-field-ServerUseSsl-dialog.png)
   ![IISU Custom Field - ServerUseSsl](docsource/images/IISU-custom-field-ServerUseSsl-validation-options-dialog.png)





   ##### Entry Parameters Tab

   | Name | Display Name | Description | Type | Default Value | Entry has a private key | Adding an entry | Removing an entry | Reenrolling an entry |
   | ---- | ------------ | ---- | ------------- | ----------------------- | ---------------- | ----------------- | ------------------- | ----------- |
   | Port | Port | String value specifying the IP port to bind the certificate to for the IIS site. Example: '443' for HTTPS. | String | 443 | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |
   | IPAddress | IP Address | String value specifying the IP address to bind the certificate to for the IIS site. Example: '*' for all IP addresses or '192.168.1.1' for a specific IP address. | String | * | 🔲 Unchecked | ✅ Checked | ✅ Checked | ✅ Checked |
   | HostName | Host Name | String value specifying the host name (host header) to bind the certificate to for the IIS site. Leave blank for all host names or enter a specific hostname such as 'www.example.com'. | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |
   | SiteName | IIS Site Name | String value specifying the name of the IIS web site to bind the certificate to. Example: 'Default Web Site' or any custom site name such as 'MyWebsite'. | String | Default Web Site | 🔲 Unchecked | ✅ Checked | ✅ Checked | ✅ Checked |
   | SniFlag | SSL Flags | A 128-Bit Flag that determines what type of SSL settings you wish to use.  The default is 0, meaning No SNI.  For more information, check IIS documentation for the appropriate bit setting.) | String | 0 | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |
   | Protocol | Protocol | Multiple choice value specifying the protocol to bind to. Example: 'https' for secure communication. | MultipleChoice | https | 🔲 Unchecked | ✅ Checked | ✅ Checked | ✅ Checked |
   | ProviderName | Crypto Provider Name | Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers' | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |

   The Entry Parameters tab should look like this:

   ![IISU Entry Parameters Tab](docsource/images/IISU-entry-parameters-store-type-dialog.png)


   ##### Port
   String value specifying the IP port to bind the certificate to for the IIS site. Example: '443' for HTTPS.

   ![IISU Entry Parameter - Port](docsource/images/IISU-entry-parameters-store-type-dialog-Port.png)
   ![IISU Entry Parameter - Port](docsource/images/IISU-entry-parameters-store-type-dialog-Port-validation-options.png)


   ##### IP Address
   String value specifying the IP address to bind the certificate to for the IIS site. Example: '*' for all IP addresses or '192.168.1.1' for a specific IP address.

   ![IISU Entry Parameter - IPAddress](docsource/images/IISU-entry-parameters-store-type-dialog-IPAddress.png)
   ![IISU Entry Parameter - IPAddress](docsource/images/IISU-entry-parameters-store-type-dialog-IPAddress-validation-options.png)


   ##### Host Name
   String value specifying the host name (host header) to bind the certificate to for the IIS site. Leave blank for all host names or enter a specific hostname such as 'www.example.com'.

   ![IISU Entry Parameter - HostName](docsource/images/IISU-entry-parameters-store-type-dialog-HostName.png)
   ![IISU Entry Parameter - HostName](docsource/images/IISU-entry-parameters-store-type-dialog-HostName-validation-options.png)


   ##### IIS Site Name
   String value specifying the name of the IIS web site to bind the certificate to. Example: 'Default Web Site' or any custom site name such as 'MyWebsite'.

   ![IISU Entry Parameter - SiteName](docsource/images/IISU-entry-parameters-store-type-dialog-SiteName.png)
   ![IISU Entry Parameter - SiteName](docsource/images/IISU-entry-parameters-store-type-dialog-SiteName-validation-options.png)


   ##### SSL Flags
   A 128-Bit Flag that determines what type of SSL settings you wish to use.  The default is 0, meaning No SNI.  For more information, check IIS documentation for the appropriate bit setting.)

   ![IISU Entry Parameter - SniFlag](docsource/images/IISU-entry-parameters-store-type-dialog-SniFlag.png)
   ![IISU Entry Parameter - SniFlag](docsource/images/IISU-entry-parameters-store-type-dialog-SniFlag-validation-options.png)


   ##### Protocol
   Multiple choice value specifying the protocol to bind to. Example: 'https' for secure communication.

   ![IISU Entry Parameter - Protocol](docsource/images/IISU-entry-parameters-store-type-dialog-Protocol.png)
   ![IISU Entry Parameter - Protocol](docsource/images/IISU-entry-parameters-store-type-dialog-Protocol-validation-options.png)


   ##### Crypto Provider Name
   Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers'

   ![IISU Entry Parameter - ProviderName](docsource/images/IISU-entry-parameters-store-type-dialog-ProviderName.png)
   ![IISU Entry Parameter - ProviderName](docsource/images/IISU-entry-parameters-store-type-dialog-ProviderName-validation-options.png)



   </details>
</details>

### WinSql

<details><summary>Click to expand details</summary>


The WinSql Certificate Store Type, referred to by its short name 'WinSql,' is designed for the management of certificates used by SQL Server instances. This store type allows users to automate the process of adding, removing, reenrolling, and inventorying certificates associated with SQL Server, thereby simplifying the management of SSL/TLS certificates for database servers.

#### Caveats and Limitations

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.




#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | ✅ Checked     |
| Discovery    | 🔲 Unchecked  |
| Reenrollment | 🔲 Unchecked |
| Create       | 🔲 Unchecked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand WinSql kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # WinSql
   kfutil store-types create WinSql
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the WinSql store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual WinSql details</summary>

   Create a store type called `WinSql` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | WinSql | Display name for the store type (may be customized) |
   | Short Name | WinSql | Short display name for the store type |
   | Capability | WinSql | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | ✅ Checked | Check the box. Indicates that the Store Type supports Management Remove |
   | Supports Discovery | 🔲 Unchecked |  Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | 🔲 Unchecked |  Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | ✅ Checked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![WinSql Basic Tab](docsource/images/WinSql-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Optional | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![WinSql Advanced Tab](docsource/images/WinSql-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |
   | spnwithport | SPN With Port | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. | Bool | false | 🔲 Unchecked |
   | WinRM Protocol | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. | MultipleChoice | https,http,ssh | ✅ Checked |
   | WinRM Port | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. | String | 5986 | ✅ Checked |
   | ServerUsername | Server Username | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerPassword | Server Password | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerUseSsl | Use SSL | Determine whether the server uses SSL or not (This field is automatically created) | Bool | true | ✅ Checked |
   | RestartService | Restart SQL Service After Cert Installed | Boolean value (true or false) indicating whether to restart the SQL Server service after installing the certificate. Example: 'true' to enable service restart after installation. | Bool | false | ✅ Checked |

   The Custom Fields tab should look like this:

   ![WinSql Custom Fields Tab](docsource/images/WinSql-custom-fields-store-type-dialog.png)


   ###### SPN With Port
   Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.

   ![WinSql Custom Field - spnwithport](docsource/images/WinSql-custom-field-spnwithport-dialog.png)
   ![WinSql Custom Field - spnwithport](docsource/images/WinSql-custom-field-spnwithport-validation-options-dialog.png)



   ###### WinRM Protocol
   Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment.

   ![WinSql Custom Field - WinRM Protocol](docsource/images/WinSql-custom-field-WinRM Protocol-dialog.png)
   ![WinSql Custom Field - WinRM Protocol](docsource/images/WinSql-custom-field-WinRM Protocol-validation-options-dialog.png)



   ###### WinRM Port
   String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22.

   ![WinSql Custom Field - WinRM Port](docsource/images/WinSql-custom-field-WinRM Port-dialog.png)
   ![WinSql Custom Field - WinRM Port](docsource/images/WinSql-custom-field-WinRM Port-validation-options-dialog.png)



   ###### Server Username
   Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Server Password
   Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Use SSL
   Determine whether the server uses SSL or not (This field is automatically created)

   ![WinSql Custom Field - ServerUseSsl](docsource/images/WinSql-custom-field-ServerUseSsl-dialog.png)
   ![WinSql Custom Field - ServerUseSsl](docsource/images/WinSql-custom-field-ServerUseSsl-validation-options-dialog.png)



   ###### Restart SQL Service After Cert Installed
   Boolean value (true or false) indicating whether to restart the SQL Server service after installing the certificate. Example: 'true' to enable service restart after installation.

   ![WinSql Custom Field - RestartService](docsource/images/WinSql-custom-field-RestartService-dialog.png)
   ![WinSql Custom Field - RestartService](docsource/images/WinSql-custom-field-RestartService-validation-options-dialog.png)





   ##### Entry Parameters Tab

   | Name | Display Name | Description | Type | Default Value | Entry has a private key | Adding an entry | Removing an entry | Reenrolling an entry |
   | ---- | ------------ | ---- | ------------- | ----------------------- | ---------------- | ----------------- | ------------------- | ----------- |
   | InstanceName | Instance Name | String value specifying the SQL Server instance name to bind the certificate to. Example: 'MSSQLServer' for the default instance or 'Instance1' for a named instance. | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |
   | ProviderName | Crypto Provider Name | Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers' | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |

   The Entry Parameters tab should look like this:

   ![WinSql Entry Parameters Tab](docsource/images/WinSql-entry-parameters-store-type-dialog.png)


   ##### Instance Name
   String value specifying the SQL Server instance name to bind the certificate to. Example: 'MSSQLServer' for the default instance or 'Instance1' for a named instance.

   ![WinSql Entry Parameter - InstanceName](docsource/images/WinSql-entry-parameters-store-type-dialog-InstanceName.png)
   ![WinSql Entry Parameter - InstanceName](docsource/images/WinSql-entry-parameters-store-type-dialog-InstanceName-validation-options.png)


   ##### Crypto Provider Name
   Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers'

   ![WinSql Entry Parameter - ProviderName](docsource/images/WinSql-entry-parameters-store-type-dialog-ProviderName.png)
   ![WinSql Entry Parameter - ProviderName](docsource/images/WinSql-entry-parameters-store-type-dialog-ProviderName-validation-options.png)



   </details>
</details>

### WinAdfs

<details><summary>Click to expand details</summary>


WinADFS is a store type designed for managing certificates within Microsoft Active Directory Federation Services (ADFS) environments. This store type enables users to automate the management of certificates used for securing ADFS communications, including tasks such as adding, removing, and renewing certificates associated with ADFS services.
* NOTE: Only the Service-Communications certificate is currently supported.  Follow your ADFS best practices for token encrypt and decrypt certificate management.
* NOTE: This extension also supports the auto-removal of expired certificates from the ADFS stores on the Primary and Secondary nodes during the certificate rotation process, along with restarting the ADFS service to apply changes.




#### ADFS Rotation Manager Requirements

When using WinADFS, the Universal Orchestrator must act as an agent and be installed on the Primary ADFS server within the ADFS farm. This is necessary because ADFS configurations and certificate management operations must be performed directly on the ADFS server itself to ensure proper functionality and security.



#### Supported Operations

| Operation    | Is Supported                                                                                                           |
|--------------|------------------------------------------------------------------------------------------------------------------------|
| Add          | ✅ Checked        |
| Remove       | 🔲 Unchecked     |
| Discovery    | 🔲 Unchecked  |
| Reenrollment | 🔲 Unchecked |
| Create       | 🔲 Unchecked     |

#### Store Type Creation

##### Using kfutil:
`kfutil` is a custom CLI for the Keyfactor Command API and can be used to create certificate store types.
For more information on [kfutil](https://github.com/Keyfactor/kfutil) check out the [docs](https://github.com/Keyfactor/kfutil?tab=readme-ov-file#quickstart)
   <details><summary>Click to expand WinAdfs kfutil details</summary>

   ##### Using online definition from GitHub:
   This will reach out to GitHub and pull the latest store-type definition
   ```shell
   # ADFS Rotation Manager
   kfutil store-types create WinAdfs
   ```

   ##### Offline creation using integration-manifest file:
   If required, it is possible to create store types from the [integration-manifest.json](./integration-manifest.json) included in this repo.
   You would first download the [integration-manifest.json](./integration-manifest.json) and then run the following command
   in your offline environment.
   ```shell
   kfutil store-types create --from-file integration-manifest.json
   ```
   </details>


#### Manual Creation
Below are instructions on how to create the WinAdfs store type manually in
the Keyfactor Command Portal
   <details><summary>Click to expand manual WinAdfs details</summary>

   Create a store type called `WinAdfs` with the attributes in the tables below:

   ##### Basic Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Name | ADFS Rotation Manager | Display name for the store type (may be customized) |
   | Short Name | WinAdfs | Short display name for the store type |
   | Capability | WinAdfs | Store type name orchestrator will register with. Check the box to allow entry of value |
   | Supports Add | ✅ Checked | Check the box. Indicates that the Store Type supports Management Add |
   | Supports Remove | 🔲 Unchecked |  Indicates that the Store Type supports Management Remove |
   | Supports Discovery | 🔲 Unchecked |  Indicates that the Store Type supports Discovery |
   | Supports Reenrollment | 🔲 Unchecked |  Indicates that the Store Type supports Reenrollment |
   | Supports Create | 🔲 Unchecked |  Indicates that the Store Type supports store creation |
   | Needs Server | ✅ Checked | Determines if a target server name is required when creating store |
   | Blueprint Allowed | ✅ Checked | Determines if store type may be included in an Orchestrator blueprint |
   | Uses PowerShell | 🔲 Unchecked | Determines if underlying implementation is PowerShell |
   | Requires Store Password | 🔲 Unchecked | Enables users to optionally specify a store password when defining a Certificate Store. |
   | Supports Entry Password | 🔲 Unchecked | Determines if an individual entry within a store can have a password. |

   The Basic tab should look like this:

   ![WinAdfs Basic Tab](docsource/images/WinAdfs-basic-store-type-dialog.png)

   ##### Advanced Tab
   | Attribute | Value | Description |
   | --------- | ----- | ----- |
   | Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias. |
   | Private Key Handling | Required | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be invalid. |
   | PFX Password Style | Default | 'Default' - PFX password is randomly generated, 'Custom' - PFX password may be specified when the enrollment job is created (Requires the Allow Custom Password application setting to be enabled.) |

   The Advanced tab should look like this:

   ![WinAdfs Advanced Tab](docsource/images/WinAdfs-advanced-store-type-dialog.png)

   > For Keyfactor **Command versions 24.4 and later**, a Certificate Format dropdown is available with PFX and PEM options. Ensure that **PFX** is selected, as this determines the format of new and renewed certificates sent to the Orchestrator during a Management job. Currently, all Keyfactor-supported Orchestrator extensions support only PFX.

   ##### Custom Fields Tab
   Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed. The following custom fields should be added to the store type:

   | Name | Display Name | Description | Type | Default Value/Options | Required |
   | ---- | ------------ | ---- | --------------------- | -------- | ----------- |
   | spnwithport | SPN With Port | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. | Bool | false | 🔲 Unchecked |
   | WinRM Protocol | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. | MultipleChoice | https,http,ssh | ✅ Checked |
   | WinRM Port | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. | String | 5986 | ✅ Checked |
   | ServerUsername | Server Username | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerPassword | Server Password | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) | Secret |  | 🔲 Unchecked |
   | ServerUseSsl | Use SSL | Determine whether the server uses SSL or not (This field is automatically created) | Bool | true | ✅ Checked |

   The Custom Fields tab should look like this:

   ![WinAdfs Custom Fields Tab](docsource/images/WinAdfs-custom-fields-store-type-dialog.png)


   ###### SPN With Port
   Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.

   ![WinAdfs Custom Field - spnwithport](docsource/images/WinAdfs-custom-field-spnwithport-dialog.png)
   ![WinAdfs Custom Field - spnwithport](docsource/images/WinAdfs-custom-field-spnwithport-validation-options-dialog.png)



   ###### WinRM Protocol
   Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment.

   ![WinAdfs Custom Field - WinRM Protocol](docsource/images/WinAdfs-custom-field-WinRM Protocol-dialog.png)
   ![WinAdfs Custom Field - WinRM Protocol](docsource/images/WinAdfs-custom-field-WinRM Protocol-validation-options-dialog.png)



   ###### WinRM Port
   String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22.

   ![WinAdfs Custom Field - WinRM Port](docsource/images/WinAdfs-custom-field-WinRM Port-dialog.png)
   ![WinAdfs Custom Field - WinRM Port](docsource/images/WinAdfs-custom-field-WinRM Port-validation-options-dialog.png)



   ###### Server Username
   Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Server Password
   Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created)


   > [!IMPORTANT]
   > This field is created by the `Needs Server` on the Basic tab, do not create this field manually.




   ###### Use SSL
   Determine whether the server uses SSL or not (This field is automatically created)

   ![WinAdfs Custom Field - ServerUseSsl](docsource/images/WinAdfs-custom-field-ServerUseSsl-dialog.png)
   ![WinAdfs Custom Field - ServerUseSsl](docsource/images/WinAdfs-custom-field-ServerUseSsl-validation-options-dialog.png)





   ##### Entry Parameters Tab

   | Name | Display Name | Description | Type | Default Value | Entry has a private key | Adding an entry | Removing an entry | Reenrolling an entry |
   | ---- | ------------ | ---- | ------------- | ----------------------- | ---------------- | ----------------- | ------------------- | ----------- |
   | ProviderName | Crypto Provider Name | Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers' | String |  | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked | 🔲 Unchecked |

   The Entry Parameters tab should look like this:

   ![WinAdfs Entry Parameters Tab](docsource/images/WinAdfs-entry-parameters-store-type-dialog.png)


   ##### Crypto Provider Name
   Name of the Windows cryptographic service provider to use when generating and storing private keys. For more information, refer to the section 'Using Crypto Service Providers'

   ![WinAdfs Entry Parameter - ProviderName](docsource/images/WinAdfs-entry-parameters-store-type-dialog-ProviderName.png)
   ![WinAdfs Entry Parameter - ProviderName](docsource/images/WinAdfs-entry-parameters-store-type-dialog-ProviderName-validation-options.png)



   </details>
</details>


## Installation

1. **Download the latest Windows Certificate Universal Orchestrator extension from GitHub.**

    Navigate to the [Windows Certificate Universal Orchestrator extension GitHub version page](https://github.com/Keyfactor/iis-orchestrator/releases/latest). Refer to the compatibility matrix below to determine the asset should be downloaded. Then, click the corresponding asset to download the zip archive.

   | Universal Orchestrator Version | Latest .NET version installed on the Universal Orchestrator server | `rollForward` condition in `Orchestrator.runtimeconfig.json` | `iis-orchestrator` .NET version to download |
   | --------- | ----------- | ----------- | ----------- |
   | Older than `11.0.0` | | | `net6.0` |
   | Between `11.0.0` and `11.5.1` (inclusive) | `net6.0` | | `net6.0` |
   | Between `11.0.0` and `11.5.1` (inclusive) | `net8.0` | `Disable` | `net6.0` || Between `11.0.0` and `11.5.1` (inclusive) | `net8.0` | `LatestMajor` | `net8.0` |
   | `11.6` _and_ newer | `net8.0` | | `net8.0` | 

    Unzip the archive containing extension assemblies to a known location.

    > **Note** If you don't see an asset with a corresponding .NET version, you should always assume that it was compiled for `net6.0`.

2. **Locate the Universal Orchestrator extensions directory.**

    * **Default on Windows** - `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions`
    * **Default on Linux** - `/opt/keyfactor/orchestrator/extensions`

3. **Create a new directory for the Windows Certificate Universal Orchestrator extension inside the extensions directory.**

    Create a new directory called `iis-orchestrator`.
    > The directory name does not need to match any names used elsewhere; it just has to be unique within the extensions directory.

4. **Copy the contents of the downloaded and unzipped assemblies from __step 2__ to the `iis-orchestrator` directory.**

5. **Restart the Universal Orchestrator service.**

    Refer to [Starting/Restarting the Universal Orchestrator service](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/StarttheService.htm).


6. **(optional) PAM Integration**

    The Windows Certificate Universal Orchestrator extension is compatible with all supported Keyfactor PAM extensions to resolve PAM-eligible secrets. PAM extensions running on Universal Orchestrators enable secure retrieval of secrets from a connected PAM provider.

    To configure a PAM provider, [reference the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) to select an extension and follow the associated instructions to install it on the Universal Orchestrator (remote).


> The above installation steps can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions).



## Defining Certificate Stores

The Windows Certificate Universal Orchestrator extension implements 4 Certificate Store Types, each of which implements different functionality. Refer to the individual instructions below for each Certificate Store Type that you deemed necessary for your use case from the installation section.

<details><summary>Windows Certificate (WinCert)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "Windows Certificate" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the certificate store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields. For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Windows certificate store path to manage. The store must exist in the Local Machine store on the target server, e.g., 'My' for the Personal Store or 'Root' for the Trusted Root Certification Authorities Store. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinCert` certificates. Specifically, one with the `WinCert` capability. |
   | spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'.  (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the WinCert certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name WinCert --outpath WinCert.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "Windows Certificate" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the certificate store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields. For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Windows certificate store path to manage. The store must exist in the Local Machine store on the target server, e.g., 'My' for the Personal Store or 'Root' for the Trusted Root Certification Authorities Store. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinCert` certificates. Specifically, one with the `WinCert` capability. |
   | Properties.spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | Properties.WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | Properties.WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | Properties.ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'.  (This field is automatically created) |
   | Properties.ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | Properties.ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name WinCert --file WinCert.csv
    ```

</details>


#### PAM Provider Eligible Fields
<details><summary>Attributes eligible for retrieval by a PAM Provider on the Universal Orchestrator</summary>

If a PAM provider was installed _on the Universal Orchestrator_ in the [Installation](#Installation) section, the following parameters can be configured for retrieval _on the Universal Orchestrator_.

   | Attribute | Description |
   | --------- | ----------- |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'.  (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |

Please refer to the **Universal Orchestrator (remote)** usage section ([PAM providers on the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam)) for your selected PAM provider for instructions on how to load attributes orchestrator-side.
> Any secret can be rendered by a PAM provider _installed on the Keyfactor Command server_. The above parameters are specific to attributes that can be fetched by an installed PAM provider running on the Universal Orchestrator server itself.

</details>


> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>IIS Bound Certificate (IISU)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "IIS Bound Certificate" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the IIS certificate store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields.  For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Windows certificate store path to manage. Choose 'My' for the Personal store or 'WebHosting' for the Web Hosting store. |
   | Orchestrator | Select an approved orchestrator capable of managing `IISU` certificates. Specifically, one with the `IISU` capability. |
   | spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the IISU certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name IISU --outpath IISU.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "IIS Bound Certificate" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the IIS certificate store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields.  For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Windows certificate store path to manage. Choose 'My' for the Personal store or 'WebHosting' for the Web Hosting store. |
   | Orchestrator | Select an approved orchestrator capable of managing `IISU` certificates. Specifically, one with the `IISU` capability. |
   | Properties.spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | Properties.WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | Properties.WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | Properties.ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | Properties.ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | Properties.ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name IISU --file IISU.csv
    ```

</details>


#### PAM Provider Eligible Fields
<details><summary>Attributes eligible for retrieval by a PAM Provider on the Universal Orchestrator</summary>

If a PAM provider was installed _on the Universal Orchestrator_ in the [Installation](#Installation) section, the following parameters can be configured for retrieval _on the Universal Orchestrator_.

   | Attribute | Description |
   | --------- | ----------- |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |

Please refer to the **Universal Orchestrator (remote)** usage section ([PAM providers on the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam)) for your selected PAM provider for instructions on how to load attributes orchestrator-side.
> Any secret can be rendered by a PAM provider _installed on the Keyfactor Command server_. The above parameters are specific to attributes that can be fetched by an installed PAM provider running on the Universal Orchestrator server itself.

</details>


> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>WinSql (WinSql)</summary>


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "WinSql" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the SQL Server Certificate Store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields. For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Fixed string value 'My' indicating the Personal store on the Local Machine. This denotes the Windows certificate store to be managed for SQL Server. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinSql` certificates. Specifically, one with the `WinSql` capability. |
   | spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |
   | RestartService | Boolean value (true or false) indicating whether to restart the SQL Server service after installing the certificate. Example: 'true' to enable service restart after installation. |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the WinSql certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name WinSql --outpath WinSql.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "WinSql" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Hostname of the Windows Server containing the SQL Server Certificate Store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields. For more information, see [Client Machine](#note-regarding-client-machine). |
   | Store Path | Fixed string value 'My' indicating the Personal store on the Local Machine. This denotes the Windows certificate store to be managed for SQL Server. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinSql` certificates. Specifically, one with the `WinSql` capability. |
   | Properties.spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | Properties.WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | Properties.WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | Properties.ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | Properties.ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | Properties.ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |
   | Properties.RestartService | Boolean value (true or false) indicating whether to restart the SQL Server service after installing the certificate. Example: 'true' to enable service restart after installation. |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name WinSql --file WinSql.csv
    ```

</details>


#### PAM Provider Eligible Fields
<details><summary>Attributes eligible for retrieval by a PAM Provider on the Universal Orchestrator</summary>

If a PAM provider was installed _on the Universal Orchestrator_ in the [Installation](#Installation) section, the following parameters can be configured for retrieval _on the Universal Orchestrator_.

   | Attribute | Description |
   | --------- | ----------- |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |

Please refer to the **Universal Orchestrator (remote)** usage section ([PAM providers on the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam)) for your selected PAM provider for instructions on how to load attributes orchestrator-side.
> Any secret can be rendered by a PAM provider _installed on the Keyfactor Command server_. The above parameters are specific to attributes that can be fetched by an installed PAM provider running on the Universal Orchestrator server itself.

</details>


> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>

<details><summary>ADFS Rotation Manager (WinAdfs)</summary>

When creating a Certificate Store for WinADFS, the Client Machine name must be set as an agent and use the LocalMachine moniker, for example: myADFSPrimary|LocalMachine.


### Store Creation

#### Manually with the Command UI

<details><summary>Click to expand details</summary>

1. **Navigate to the _Certificate Stores_ page in Keyfactor Command.**

    Log into Keyfactor Command, toggle the _Locations_ dropdown, and click _Certificate Stores_.

2. **Add a Certificate Store.**

    Click the Add button to add a new Certificate Store. Use the table below to populate the **Attributes** in the **Add** form.

   | Attribute | Description                                             |
   | --------- |---------------------------------------------------------|
   | Category | Select "ADFS Rotation Manager" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Since this extension type must run as an agent (The UO Must be installed on the PRIMARY ADFS Server), the ClientMachine must follow the naming convention as outlined in the Client Machine Instructions. Secondary ADFS Nodes will be automatically be updated with the same certificate added on the PRIMARY ADFS server. |
   | Store Path | Fixed string value of 'My' indicating the Personal store on the Local Machine. All ADFS Service-Communications certificates are located in the 'My' personal store by default. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinAdfs` certificates. Specifically, one with the `WinAdfs` capability. |
   | spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

</details>



#### Using kfutil CLI

<details><summary>Click to expand details</summary>

1. **Generate a CSV template for the WinAdfs certificate store**

    ```shell
    kfutil stores import generate-template --store-type-name WinAdfs --outpath WinAdfs.csv
    ```
2. **Populate the generated CSV file**

    Open the CSV file, and reference the table below to populate parameters for each **Attribute**.

   | Attribute | Description |
   | --------- | ----------- |
   | Category | Select "ADFS Rotation Manager" or the customized certificate store name from the previous step. |
   | Container | Optional container to associate certificate store with. |
   | Client Machine | Since this extension type must run as an agent (The UO Must be installed on the PRIMARY ADFS Server), the ClientMachine must follow the naming convention as outlined in the Client Machine Instructions. Secondary ADFS Nodes will be automatically be updated with the same certificate added on the PRIMARY ADFS server. |
   | Store Path | Fixed string value of 'My' indicating the Personal store on the Local Machine. All ADFS Service-Communications certificates are located in the 'My' personal store by default. |
   | Orchestrator | Select an approved orchestrator capable of managing `WinAdfs` certificates. Specifically, one with the `WinAdfs` capability. |
   | Properties.spnwithport | Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations. |
   | Properties.WinRM Protocol | Multiple choice value specifying which protocol to use.  Protocols https or http use WinRM to connect from Windows to Windows Servers.  Using ssh is only supported when running the orchestrator in a Linux environment. |
   | Properties.WinRM Port | String value specifying the port number that the Windows target server's WinRM listener is configured to use. Example: '5986' for HTTPS or '5985' for HTTP.  By default, when using ssh in a Linux environment, the default port number is 22. |
   | Properties.ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | Properties.ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |
   | Properties.ServerUseSsl | Determine whether the server uses SSL or not (This field is automatically created) |

3. **Import the CSV file to create the certificate stores**

    ```shell
    kfutil stores import csv --store-type-name WinAdfs --file WinAdfs.csv
    ```

</details>


#### PAM Provider Eligible Fields
<details><summary>Attributes eligible for retrieval by a PAM Provider on the Universal Orchestrator</summary>

If a PAM provider was installed _on the Universal Orchestrator_ in the [Installation](#Installation) section, the following parameters can be configured for retrieval _on the Universal Orchestrator_.

   | Attribute | Description |
   | --------- | ----------- |
   | ServerUsername | Username used to log into the target server for establishing the WinRM session. Example: 'administrator' or 'domain\username'. (This field is automatically created) |
   | ServerPassword | Password corresponding to the Server Username used to log into the target server.  When establishing a SSH session from a Linux environment, the password must include the full SSH Private key. (This field is automatically created) |

Please refer to the **Universal Orchestrator (remote)** usage section ([PAM providers on the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam)) for your selected PAM provider for instructions on how to load attributes orchestrator-side.
> Any secret can be rendered by a PAM provider _installed on the Keyfactor Command server_. The above parameters are specific to attributes that can be fetched by an installed PAM provider running on the Universal Orchestrator server itself.

</details>


> The content in this section can be supplemented by the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/ReferenceGuide/Certificate%20Stores.htm?Highlight=certificate%20store).


</details>



## Client Machine Instructions
Prior to version 2.6, this extension would only run in the Windows environment.  Version 2.6 and greater is capable of running on Linux, however, only the SSH protocol is supported.

If running as an agent (accessing stores on the server where the Universal Orchestrator Services is installed ONLY), the Client Machine can be entered, OR you can bypass a WinRM connection and access the local file system directly by adding "|LocalMachine" to the end of your value for Client Machine, for example "1.1.1.1|LocalMachine".  In this instance the value to the left of the pipe (|) is ignored.  It is important to make sure the values for Client Machine and Store Path together are unique for each certificate store created, as Keyfactor Command requires the Store Type you select, along with Client Machine, and Store Path together must be unique.  To ensure this, it is good practice to put the full DNS or IP Address to the left of the | character when setting up a certificate store that will be accessed without a WinRM connection.


## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Universal Orchestrator extensions](https://github.com/orgs/Keyfactor/repositories?q=orchestrator).