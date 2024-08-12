<h1 align="center" style="border-bottom: none">
    WinCertStore Universal Orchestrator Extension
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

The IIS/Windows Certificate Universal Orchestrator extension facilitates the remote management of certificates in the Windows Server local machine certificate store. Users can specify the precise certificate store to place certificates by using an associated store type and providing the correct store path. For a comprehensive list of local machine certificate stores, you can execute the PowerShell command `Get-ChildItem Cert:\LocalMachine`. The returned list will provide the actual certificate store name to be used when entering store location.

By default, most certificates are stored in the "Personal" (My) and "Web Hosting" (WebHosting) stores. This extension supports four types of jobs: Inventory, Management Add/Remove, and Reenrollment. These jobs enable users to download all certificates, add new certificates, remove existing certificates, and reenroll certificates within the specified certificate stores.

WinRM is used for remote management of the certificate stores and IIS bindings. Proper configuration of WinRM is necessary to allow the orchestrator to manage certificates on the server.

### Certificate Store Types

The IIS/Windows Certificate Universal Orchestrator extension handles three main types of Certificate Store Types: IISU, WinCert, and WinSql.

- **IISU (IIS Bound Certificates):** Applied to IIS servers, allowing certificates to be bound to IIS sites. This type requires more specific configuration, including site names, IP addresses, ports, and support for Server Name Indication (SNI). 

- **WinCert (Windows Certificates):** Used for general Windows certificates management. It generally involves less configuration compared to IISU and is suitable for managing certificates in standard Windows certificate stores.

- **WinSql (SQL Server Certificates):** Specifically targets SQL Server management, ensuring that certificates are properly bound to SQL Server instances. It includes configurations unique to SQL Server, such as the instance name and whether the SQL service should restart after certificate installation.

> **Note:**
> In version 2.0 of the IIS Orchestrator, the certificate store type has been renamed and additional parameters have been added. Prior to 2.0 the certificate store type was called “IISBin” and as of 2.0 it is called “IISU”. If you have existing certificate stores of type “IISBin”, you have three options:
> 1. Leave them as is and continue to manage them with a pre 2.0 IIS Orchestrator Extension. Create the new IISU certificate store type and create any new IIS stores using the new type.
> 1. Delete existing IIS stores. Delete the IISBin store type. Create the new IISU store type. Recreate the IIS stores using the new IISU store type.
> 1. Convert existing IISBin certificate stores to IISU certificate stores. There is not currently a way to do this via the Keyfactor API, so direct updates to the underlying Keyfactor SQL database is required. A SQL script (IIS-Conversion.sql) is available in the repository to do this. Hosted customers, which do not have access to the underlying database, will need to work Keyfactor support to run the conversion. On-premises customers can run the script themselves, but are strongly encouraged to ensure that a SQL backup is taken prior running the script (and also be confident that they have a tested database restoration process.)
>
> **Note: There is an additional (and deprecated) certificate store type of “IIS” that ships with the Keyfactor platform. Migration of certificate stores from the “IIS” type to either the “IISBin” or “IISU” types is not currently supported.**
>
> **Note: If Looking to use GMSA Accounts to run the Service Keyfactor Command 10.2 or greater is required for No Value checkbox to work**

## Compatibility

This integration is compatible with Keyfactor Universal Orchestrator version 10.1 and later.

## Support
The WinCertStore Universal Orchestrator extension is supported by Keyfactor for Keyfactor customers. If you have a support issue, please open a support ticket with your Keyfactor representative. If you have a support issue, please open a support ticket via the Keyfactor Support Portal at https://support.keyfactor.com. 
 
> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.

## Installation
Before installing the WinCertStore Universal Orchestrator extension, it's recommended to install [kfutil](https://github.com/Keyfactor/kfutil). Kfutil is a command-line tool that simplifies the process of creating store types, installing extensions, and instantiating certificate stores in Keyfactor Command.

The WinCertStore Universal Orchestrator extension implements 3 Certificate Store Types. Depending on your use case, you may elect to install one, or all of these Certificate Store Types. An overview for each type is linked below:
* [Windows Certificate](docs/wincert.md)
* [IIS Bound Certificate](docs/iisu.md)
* [WinSql](docs/winsql.md)

<details><summary>Windows Certificate</summary>


1. Follow the [requirements section](docs/wincert.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    ### Security and Permission Considerations

    From an official support point of view, Local Administrator permissions are required on the target server. Some customers have been successful with using other accounts and granting rights to the underlying certificate and private key stores. Due to complexities with the interactions between Group Policy, WinRM, User Account Control, and other unpredictable customer environmental factors, Keyfactor cannot provide assistance with using accounts other than the local administrator account.
     
    For customers wishing to use something other than the local administrator account, the following information may be helpful:
     
    *	The WinCert extensions (WinCert, IISU, WinSQL) create a WinRM (remote PowerShell) session to the target server in order to manipulate the Windows Certificate Stores, perform binding (in the case of the IISU extension), or to access the registry (in the case of the WinSQL extension). 
     
    *	When the WinRM session is created, the certificate store credentials are used if they have been specified, otherwise the WinRM session is created in the context of the Universal Orchestrator (UO) Service account (which potentially could be the network service account, a regular account, or a GMSA account)
     
    *	WinRM needs to be properly set up between the server hosting the UO and the target server. This means that a WinRM client running on the UO server when running in the context of the UO service account needs to be able to create a session on the target server using the configured credentials of the target server and any PowerShell commands running on the remote session need to have appropriate permissions. 
     
    *	Even though a given account may be in the administrators group or have administrative privileges on the target system and may be able to execute certificate and binding operations when running locally, the same account may not work when being used via WinRM. User Account Control (UAC) can get in the way and filter out administrative privledges. UAC / WinRM configuration has a LocalAccountTokenFilterPolicy setting that can be adjusted to not filter out administrative privledges for remote users, but enabling this may have other security ramifications. 
     
    *	The following list may not be exhaustive, but in general the account (when running under a remote WinRM session) needs permissions to:
        -	Instantiate and open a .NET X509Certificates.X509Store object for the target certificate store and be able to read and write both the certificates and related private keys. Note that ACL permissions on the stores and private keys are separate.
        -	Use the Import-Certificate, Get-WebSite, Get-WebBinding, and New-WebBinding PowerShell CmdLets.
        -	Create and delete temporary files.
        -	Execute certreq commands.
        -	Access any Cryptographic Service Provider (CSP) referenced in re-enrollment jobs.
        -	Read and Write values in the registry (HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server) when performing SQL Server certificate binding.

    </details>

2. Create Certificate Store Types for the WinCertStore Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # Windows Certificate
        kfutil store-types create WinCert
        ```

    * **Manually**:
        * [Windows Certificate](docs/wincert.md#certificate-store-type-configuration)

3. Install the WinCertStore Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e iis-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e iis-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [WinCertStore Universal Orchestrator extension](https://github.com/Keyfactor/iis-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [Windows Certificate](docs/wincert.md#certificate-store-configuration)


5. (optional) Windows Certificate is compatible with all supported Keyfactor PAM extensions. PAM extensions running on Universal Orchestrators enable secure retrieval of secrets from a connected PAM provider. The [Windows Certificate certificate store configuration](docs/wincert.md#certificate-store-configuration) section marks the configuration fields that can be resolved by a PAM extension.

    To configure a PAM provider, [reference the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) to select an extension, and follow the associated instructions to install it on the Universal Orchestrator (remote).


</details>

<details><summary>IIS Bound Certificate</summary>


1. Follow the [requirements section](docs/iisu.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    ### Security and Permission Considerations

    From an official support point of view, Local Administrator permissions are required on the target server. Some customers have been successful with using other accounts and granting rights to the underlying certificate and private key stores. Due to complexities with the interactions between Group Policy, WinRM, User Account Control, and other unpredictable customer environmental factors, Keyfactor cannot provide assistance with using accounts other than the local administrator account.
     
    For customers wishing to use something other than the local administrator account, the following information may be helpful:
     
    *	The WinCert extensions (WinCert, IISU, WinSQL) create a WinRM (remote PowerShell) session to the target server in order to manipulate the Windows Certificate Stores, perform binding (in the case of the IISU extension), or to access the registry (in the case of the WinSQL extension). 
     
    *	When the WinRM session is created, the certificate store credentials are used if they have been specified, otherwise the WinRM session is created in the context of the Universal Orchestrator (UO) Service account (which potentially could be the network service account, a regular account, or a GMSA account)
     
    *	WinRM needs to be properly set up between the server hosting the UO and the target server. This means that a WinRM client running on the UO server when running in the context of the UO service account needs to be able to create a session on the target server using the configured credentials of the target server and any PowerShell commands running on the remote session need to have appropriate permissions. 
     
    *	Even though a given account may be in the administrators group or have administrative privileges on the target system and may be able to execute certificate and binding operations when running locally, the same account may not work when being used via WinRM. User Account Control (UAC) can get in the way and filter out administrative privledges. UAC / WinRM configuration has a LocalAccountTokenFilterPolicy setting that can be adjusted to not filter out administrative privledges for remote users, but enabling this may have other security ramifications. 
     
    *	The following list may not be exhaustive, but in general the account (when running under a remote WinRM session) needs permissions to:
        -	Instantiate and open a .NET X509Certificates.X509Store object for the target certificate store and be able to read and write both the certificates and related private keys. Note that ACL permissions on the stores and private keys are separate.
        -	Use the Import-Certificate, Get-WebSite, Get-WebBinding, and New-WebBinding PowerShell CmdLets.
        -	Create and delete temporary files.
        -	Execute certreq commands.
        -	Access any Cryptographic Service Provider (CSP) referenced in re-enrollment jobs.
        -	Read and Write values in the registry (HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server) when performing SQL Server certificate binding.

    </details>

2. Create Certificate Store Types for the WinCertStore Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # IIS Bound Certificate
        kfutil store-types create IISU
        ```

    * **Manually**:
        * [IIS Bound Certificate](docs/iisu.md#certificate-store-type-configuration)

3. Install the WinCertStore Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e iis-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e iis-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [WinCertStore Universal Orchestrator extension](https://github.com/Keyfactor/iis-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [IIS Bound Certificate](docs/iisu.md#certificate-store-configuration)


5. (optional) IIS Bound Certificate is compatible with all supported Keyfactor PAM extensions. PAM extensions running on Universal Orchestrators enable secure retrieval of secrets from a connected PAM provider. The [IIS Bound Certificate certificate store configuration](docs/iisu.md#certificate-store-configuration) section marks the configuration fields that can be resolved by a PAM extension.

    To configure a PAM provider, [reference the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) to select an extension, and follow the associated instructions to install it on the Universal Orchestrator (remote).


</details>

<details><summary>WinSql</summary>


1. Follow the [requirements section](docs/winsql.md#requirements) to configure a Service Account and grant necessary API permissions.

    <details><summary>Requirements</summary>

    ### Security and Permission Considerations

    From an official support point of view, Local Administrator permissions are required on the target server. Some customers have been successful with using other accounts and granting rights to the underlying certificate and private key stores. Due to complexities with the interactions between Group Policy, WinRM, User Account Control, and other unpredictable customer environmental factors, Keyfactor cannot provide assistance with using accounts other than the local administrator account.
     
    For customers wishing to use something other than the local administrator account, the following information may be helpful:
     
    *	The WinCert extensions (WinCert, IISU, WinSQL) create a WinRM (remote PowerShell) session to the target server in order to manipulate the Windows Certificate Stores, perform binding (in the case of the IISU extension), or to access the registry (in the case of the WinSQL extension). 
     
    *	When the WinRM session is created, the certificate store credentials are used if they have been specified, otherwise the WinRM session is created in the context of the Universal Orchestrator (UO) Service account (which potentially could be the network service account, a regular account, or a GMSA account)
     
    *	WinRM needs to be properly set up between the server hosting the UO and the target server. This means that a WinRM client running on the UO server when running in the context of the UO service account needs to be able to create a session on the target server using the configured credentials of the target server and any PowerShell commands running on the remote session need to have appropriate permissions. 
     
    *	Even though a given account may be in the administrators group or have administrative privileges on the target system and may be able to execute certificate and binding operations when running locally, the same account may not work when being used via WinRM. User Account Control (UAC) can get in the way and filter out administrative privledges. UAC / WinRM configuration has a LocalAccountTokenFilterPolicy setting that can be adjusted to not filter out administrative privledges for remote users, but enabling this may have other security ramifications. 
     
    *	The following list may not be exhaustive, but in general the account (when running under a remote WinRM session) needs permissions to:
        -	Instantiate and open a .NET X509Certificates.X509Store object for the target certificate store and be able to read and write both the certificates and related private keys. Note that ACL permissions on the stores and private keys are separate.
        -	Use the Import-Certificate, Get-WebSite, Get-WebBinding, and New-WebBinding PowerShell CmdLets.
        -	Create and delete temporary files.
        -	Execute certreq commands.
        -	Access any Cryptographic Service Provider (CSP) referenced in re-enrollment jobs.
        -	Read and Write values in the registry (HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server) when performing SQL Server certificate binding.

    </details>

2. Create Certificate Store Types for the WinCertStore Orchestrator extension. 

    * **Using kfutil**:

        ```shell
        # WinSql
        kfutil store-types create WinSql
        ```

    * **Manually**:
        * [WinSql](docs/winsql.md#certificate-store-type-configuration)

3. Install the WinCertStore Universal Orchestrator extension.
    
    * **Using kfutil**: On the server that that hosts the Universal Orchestrator, run the following command:

        ```shell
        # Windows Server
        kfutil orchestrator extension -e iis-orchestrator@latest --out "C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions"

        # Linux
        kfutil orchestrator extension -e iis-orchestrator@latest --out "/opt/keyfactor/orchestrator/extensions"
        ```

    * **Manually**: Follow the [official Command documentation](https://software.keyfactor.com/Core-OnPrem/Current/Content/InstallingAgents/NetCoreOrchestrator/CustomExtensions.htm?Highlight=extensions) to install the latest [WinCertStore Universal Orchestrator extension](https://github.com/Keyfactor/iis-orchestrator/releases/latest).

4. Create new certificate stores in Keyfactor Command for the Sample Universal Orchestrator extension.

    * [WinSql](docs/winsql.md#certificate-store-configuration)


5. (optional) WinSql is compatible with all supported Keyfactor PAM extensions. PAM extensions running on Universal Orchestrators enable secure retrieval of secrets from a connected PAM provider. The [WinSql certificate store configuration](docs/winsql.md#certificate-store-configuration) section marks the configuration fields that can be resolved by a PAM extension.

    To configure a PAM provider, [reference the Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) to select an extension, and follow the associated instructions to install it on the Universal Orchestrator (remote).


</details>


## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Universal Orchestrator extensions](https://github.com/orgs/Keyfactor/repositories?q=orchestrator).