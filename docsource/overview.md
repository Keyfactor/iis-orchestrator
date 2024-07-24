## Overview

The WinCertStore Universal Orchestrator extension facilitates the remote management of certificates in the Windows Server local machine certificate store. Users can specify the precise certificate store to place certificates by providing the correct store path. For a comprehensive list of local machine certificate stores, you can execute the PowerShell command `Get-ChildItem Cert:\LocalMachine`. The returned list will provide the actual certificate store name to be used when entering store location.

By default, most certificates are stored in the "Personal" (My) and "Web Hosting" (WebHosting) stores. This extension supports four types of jobs: Inventory, Management Add/Remove, and Reenrollment. These jobs enable users to download all certificates, add new certificates, remove existing certificates, and reenroll certificates within the specified certificate stores.

WinRM is used for remote management of the certificate stores and IIS bindings. Proper configuration of WinRM is necessary to allow the orchestrator to manage certificates on the server.

### Certificate Store Types

The WinCertStore Universal Orchestrator extension handles three main types of Certificate Store Types: IISU, WinCert, and WinSql.

- **IISU (IIS Bound Certificates):** Applied to IIS servers, allowing certificates to be bound to IIS sites. This type requires more specific configuration, including site names, IP addresses, ports, and support for Server Name Indication (SNI). 

- **WinCert (Windows Certificates):** Used for general Windows certificates management. It generally involves less configuration compared to IISU and is suitable for managing certificates in standard Windows certificate stores.

- **WinSql (SQL Server Certificates):** Specifically targets SQL Server management, ensuring that certificates are properly bound to SQL Server instances. It includes configurations unique to SQL Server, such as the instance name and whether the SQL service should restart after certificate installation.

Each Certificate Store Type differs in terms of its configuration parameters and the specific use-cases they address. IISU is more tailored for web server environments, whereas WinCert is used for broader Windows environments, and WinSql is focused on database server scenarios.

