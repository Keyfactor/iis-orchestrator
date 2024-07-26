## Overview

The WinSql Certificate Store Type, referred to by its short name 'WinSql,' is designed for the management of certificates used by SQL Server instances. This store type allows users to automate the process of adding, removing, reenrolling, and inventorying certificates associated with SQL Server, thereby simplifying the management of SSL/TLS certificates for database servers.

### Caveats and Limitations

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.

## Requirements

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

## Extension Mechanics

#### Note Regarding Client Machine

If the IIS Universal Orchestrator is deployed on the same server as the certificates it manages, the Client Machine field can be configured to bypass WinRM and manage the certificates directly. To do this, append `|LocalMachine` to the end of the Client Machine value. 

In Keyfactor Command, the Client Machine and Store Path fields together must be unique among all Certificate Stores of a given type. For example, the following scenario is not valid in Command:

```yaml
- Orchestrator: machineA
  ClientMachine: |LocalMachine
  StorePath: My
- Orchestrator: machineB
  ClientMachine: |LocalMachine
  StorePath: My
```

To accomodate this use-case, we recommend prepending the target machine's FQDN or IP address to the Client Machine field. 

```yaml
- Orchestrator: machineA
  ClientMachine: 1.1.1.1|LocalMachine
  StorePath: My
- Orchestrator: machineB
  ClientMachine: 2.2.2.2|LocalMachine
  StorePath: My
```

> All characters before the `|` are ignored by the extension.

## Test Cases

| Case Number | Case Name                                                         | Enrollment Params                        | Expected Results                                                                                                               | Passed | Screenshot                   |
|-------------|-------------------------------------------------------------------|------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------|--------|------------------------------|
| 1	          | New Cert Enrollment To Default Instance Leave Blank               | **Instance Name:**                       | Cert will be Installed to default Instance, Service will be restarted for default instance                                     | True   | ![](../docsource/images/SQLTestCase1.gif) |
| 2	          | New Cert Enrollment To Default Instance MSSQLServer               | **Instance Name:** MSSQLServer           | Cert will be Installed to default Instance, Service will be restarted for default instance                                     | True   | ![](../docsource/images/SQLTestCase2.gif) |
| 3	          | New Cert Enrollment To Instance1                                  | **Instance Name:** Instance1             | Cert will be Installed to Instance1, Service will be restarted for Instance1                                                   | True   | ![](../docsource/images/SQLTestCase3.gif) |
| 4	          | New Cert Enrollment To Instance1 and Default Instance             | **Instance Name:** MSSQLServer,Instance1 | Cert will be Installed to Default Instance and Instance1, Service will be restarted for Default Instance and Instance1         | True   | ![](../docsource/images/SQLTestCase4.gif) |
| 5	          | One Click Renew Cert Enrollment To Instance1 and Default Instance | N/A                                      | Cert will be Renewed/Installed to Default Instance and Instance1, Service will be restarted for Default Instance and Instance1 | True   | ![](../docsource/images/SQLTestCase5.gif) |
| 6	          | Remove Cert From Instance1 and Default Instance                   | **Instance Name:**                       | Cert from TC5 will be Removed From Default Instance and Instance1                                                              | True   | ![](../docsource/images/SQLTestCase6.gif) |
| 7	          | Inventory Different Certs Different Instance                      | N/A                                      | 2 Certs will be inventoried and each tied to its Instance                                                                      | True   | ![](../docsource/images/SQLTestCase7.gif) |
| 8	          | Inventory Same Cert Different Instance                            | N/A                                      | 2 Certs will be inventoried the cert will have a comma separated list of Instances                                             | True   | ![](../docsource/images/SQLTestCase8.gif) |
| 9	          | Inventory Against Machine Without SQL Server                      | N/A                                      | Will fail with error saying it can't find SQL Server                                                                           | True   | ![](../docsource/images/SQLTestCase9.gif) |
