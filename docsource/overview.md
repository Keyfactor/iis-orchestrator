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
