# WinCertStore Orchestrator

The Windows Certificate Store Orchestrator Extension implements two certificate store types. 1) “WinCert” which manages certificates in a Windows local machine store, and 2) “IISU” which manages certificates and their bindings in a Windows local machine store that are bound to Internet Information Server (IIS) websites. These extensions replace the now deprecated “IIS” cert store type that ships with Keyfactor Command. The “IISU” extension also replaces the “IISBin” certificate store type from prior versions of this repository. This orchestrator extension is in the process of being renamed from “IIS Orchestrator” as it now supports certificates that are not in use by IIS.

#### Integration status: Production - Ready for use in production environments.


## About the Keyfactor Universal Orchestrator Extension

This repository contains a Universal Orchestrator Extension which is a plugin to the Keyfactor Universal Orchestrator. Within the Keyfactor Platform, Orchestrators are used to manage “certificate stores” &mdash; collections of certificates and roots of trust that are found within and used by various applications.

The Universal Orchestrator is part of the Keyfactor software distribution and is available via the Keyfactor customer portal. For general instructions on installing Extensions, see the “Keyfactor Command Orchestrator Installation and Configuration Guide” section of the Keyfactor documentation. For configuration details of this specific Extension see below in this readme.

The Universal Orchestrator is the successor to the Windows Orchestrator. This Orchestrator Extension plugin only works with the Universal Orchestrator and does not work with the Windows Orchestrator.


## Support for WinCertStore Orchestrator

WinCertStore Orchestrator 

###### To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.


---




## Keyfactor Version Supported

The minimum version of the Keyfactor Universal Orchestrator Framework needed to run this version of the extension is 10.1

## Platform Specific Notes

The Keyfactor Universal Orchestrator may be installed on either Windows or Linux based platforms. The certificate operations supported by a capability may vary based what platform the capability is installed on. The table below indicates what capabilities are supported based on which platform the encompassing Universal Orchestrator is running.
| Operation | Win | Linux |
|-----|-----|------|
|Supports Management Add|&check; |  |
|Supports Management Remove|&check; |  |
|Supports Create Store|  |  |
|Supports Discovery|  |  |
|Supports Renrollment|&check; |  |
|Supports Inventory|&check; |  |


## PAM Integration

This orchestrator extension has the ability to connect to a variety of supported PAM providers to allow for the retrieval of various client hosted secrets right from the orchestrator server itself.  This eliminates the need to set up the PAM integration on Keyfactor Command which may be in an environment that the client does not want to have access to their PAM provider.

The secrets that this orchestrator extension supports for use with a PAM Provider are:

|Name|Description|
|----|-----------|
|Server Username|The user id that will be used to authenticate into the server hosting the store|
|Server Password|The password that will be used to authenticate into the server hosting the store|


It is not necessary to use a PAM Provider for all of the secrets available above. If a PAM Provider should not be used, simply enter in the actual value to be used, as normal.

If a PAM Provider will be used for one of the fields above, start by referencing the [Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam). The GitHub repo for the PAM Provider to be used contains important information such as the format of the `json` needed. What follows is an example but does not reflect the `json` values for all PAM Providers as they have different "instance" and "initialization" parameter names and values.

<details><summary>General PAM Provider Configuration</summary>
<p>



### Example PAM Provider Setup

To use a PAM Provider to resolve a field, in this example the __Server Password__ will be resolved by the `Hashicorp-Vault` provider, first install the PAM Provider extension from the [Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) on the Universal Orchestrator.

Next, complete configuration of the PAM Provider on the UO by editing the `manifest.json` of the __PAM Provider__ (e.g. located at extensions/Hashicorp-Vault/manifest.json). The "initialization" parameters need to be entered here:

~~~ json
  "Keyfactor:PAMProviders:Hashicorp-Vault:InitializationInfo": {
    "Host": "http://127.0.0.1:8200",
    "Path": "v1/secret/data",
    "Token": "xxxxxx"
  }
~~~

After these values are entered, the Orchestrator needs to be restarted to pick up the configuration. Now the PAM Provider can be used on other Orchestrator Extensions.

### Use the PAM Provider
With the PAM Provider configured as an extenion on the UO, a `json` object can be passed instead of an actual value to resolve the field with a PAM Provider. Consult the [Keyfactor Integration Catalog](https://keyfactor.github.io/integrations-catalog/content/pam) for the specific format of the `json` object.

To have the __Server Password__ field resolved by the `Hashicorp-Vault` provider, the corresponding `json` object from the `Hashicorp-Vault` extension needs to be copied and filed in with the correct information:

~~~ json
{"Secret":"my-kv-secret","Key":"myServerPassword"}
~~~

This text would be entered in as the value for the __Server Password__, instead of entering in the actual password. The Orchestrator will attempt to use the PAM Provider to retrieve the __Server Password__. If PAM should not be used, just directly enter in the value for the field.
</p>
</details> 




---


# WinCertStore Orchestrator Configuration
## Overview

The WinCertStore Orchestrator remotely manages certificates in a Windows Server local machine certificate store.  Users are able to determine which store they wish to place certificates in by entering the correct store path.  For a complete list of local machine cert stores you can execute the PowerShell command:

	Get-ChildItem Cert:\LocalMachine

The returned list will contain the actual certificate store name to be used when entering store location.

By default, most certificates are stored in the “Personal” (My) and “Web Hosting” (WebHosting) stores.

This extension implements four job types:  Inventory, Management Add/Remove, and ReEnrollment.

WinRM is used to remotely manage the certificate stores and IIS bindings.  WinRM must be properly configured to allow the orchestrator on the server to manage the certificates.  Setting up WinRM is not in the scope of this document.

**Note:**
In version 2.0 of the IIS Orchestrator, the certificate store type has been renamed and additional parameters have been added. Prior to 2.0 the certificate store type was called “IISBin” and as of 2.0 it is called “IISU”. If you have existing certificate stores of type “IISBin”, you have three options:
1. Leave them as is and continue to manage them with a pre 2.0 IIS Orchestrator Extension. Create the new IISU certificate store type and create any new IIS stores using the new type.
1. Delete existing IIS stores. Delete the IISBin store type. Create the new IISU store type. Recreate the IIS stores using the new IISU store type.
1. Convert existing IISBin certificate stores to IISU certificate stores. There is not currently a way to do this via the Keyfactor API, so direct updates to the underlying Keyfactor SQL database is required. A SQL script (IIS-Conversion.sql) is available in the repository to do this. Hosted customers, which do not have access to the underlying database, will need to work Keyfactor support to run the conversion. On-premises customers can run the script themselves, but are strongly encouraged to ensure that a SQL backup is taken prior running the script (and also be confident that they have a tested database restoration process.)

**Note: There is an additional (and deprecated) certificate store type of “IIS” that ships with the Keyfactor platform. Migration of certificate stores from the “IIS” type to either the “IISBin” or “IISU” types is not currently supported.**

**Note: If Looking to use GMSA Accounts to run the Service Kefyactor Command 10.2 or greater is required for No Value checkbox to work**

## Creating New Certificate Store Types
Currently this orchestrator handles two extensions: IISU for IIS servers with bound certificates and WinCert for general Windows Certificates.
Below describes how each of these certificate store types are created and configured.
<details>
	<summary>IISU Extension</summary>

**In Keyfactor Command create a new Certificate Store Type as specified below:**

**Basic Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Name | IIS Bound Certificate | Display name for the store type (may be customized)
Short Name| IISU | Short display name for the store type
Custom Capability | IISU | Store type name orchestrator will register with. Check the box to allow entry of value
Supported Job Types | Inventory, Add, Remove, Reenrollment | Job types the extension supports
Needs Server | Checked | Determines if a target server name is required when creating store
Blueprint Allowed | Unchecked | Determines if store type may be included in an Orchestrator blueprint
Uses PowerShell | Unchecked | Determines if underlying implementation is PowerShell
Requires Store Password	| Unchecked | Determines if a store password is required when configuring an individual store.
Supports Entry Password	| Unchecked | Determines if an individual entry within a store can have a password.

![](images/IISUCertStoreBasic.png)

**Advanced Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Store Path Type	| Multiple Choice | Determines what restrictions are applied to the store path field when configuring a new store.
Store Path Value | My,WebHosting | Comma separated list of options configure multiple choice. This, combined with the hostname, will determine the location used for the certificate store management and inventory.
Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias.
Private Keys | Required | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be useless.
PFX Password Style | Default or Custom | "Default" - PFX password is randomly generated, "Custom" - PFX password may be specified when the enrollment job is created (Requires the *Allow Custom Password* application setting to be enabled.)

![](images/IISUCertStoreAdv.png)

**Custom Fields:**

Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote
target server containing the certificate store to be managed

Name|Display Name|Type|Default Value / Options|Required|Description
---|---|---|---|---|---
WinRm Protocol|WinRm Protocol|Multiple Choice| https,http |Yes|Protocol that target server WinRM listener is using
WinRm Port|WinRm Port|String|5986|Yes| Port that target server WinRM listener is using. Typically 5985 for HTTP and 5986 for HTTPS
spnwithport|SPN With Port|Bool|false|No|Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.
ServerUsername|Server Username|Secret||No|The username to log into the target server (This field is automatically created).   Check the No Value Checkbox when using GMSA Accounts.
ServerPassword|Server Password|Secret||No|The password that matches the username to log into the target server (This field is automatically created).  Check the No Value Checkbox when using GMSA Accounts.
ServerUseSsl|Use SSL|Bool|true|Yes|Determine whether the server uses SSL or not (This field is automatically created)

*Note that some of the Names in the first column above have spaces and some do not, it is important to configure the Name field exactly as above.*


![](images/IISUCustomFields.png)

**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.

Name|Display Name| Type|Default Value|Required When|Description
---|---|---|---|---|---
SiteName | IIS Site Name|String|Default Web Site|Adding, Removing, Reenrolling | IIS web site to bind certificate to
IPAddress | IP Address | String | * | Adding, Removing, Reenrolling | IP address to bind certificate to (use '*' for all IP addresses)
Port | Port | String | 443 || Adding, Removing, Reenrolling|IP port for bind certificate to
HostName | Host Name | String |||| Host name (host header) to bind certificate to, leave blank for all host names
SniFlag | SNI Support | Multiple Choice | 0 - No SNI||Type of SNI for binding<br>(Multiple choice configuration should be entered as "0 - No SNI,1 - SNI Enabled,2 - Non SNI Binding,3 - SNI Binding")
Protocol | Protocol | Multiple Choice | https| Adding, Removing, Reenrolling|Protocol to bind to (always "https").<br>(Multiple choice configuration should be "https") 
ProviderName | Crypto Provider Name | String ||| Name of the Windows cryptographic provider to use during reenrollment jobs when generating and storing the private keys. If not specified, defaults to 'Microsoft Strong Cryptographic Provider'. This value would typically be specified when leveraging a Hardware Security Module (HSM). The specified cryptographic provider must be available on the target server being managed. The list of installed cryptographic providers can be obtained by running 'certutil -csplist' on the target Server.
SAN | SAN | String || Reenrolling | Specifies Subject Alternative Name (SAN) to be used when performing reenrollment jobs. Certificate templates generally require a SAN that matches the subject of the certificate (per RFC 2818). Format is a list of <san_type>=<san_value> entries separated by ampersands. Examples: 'dns=www.mysite.com' for a single SAN or 'dns=www.mysite.com&dns=www.mysite2.com' for multiple SANs. Can be made optional if RFC 2818 is disabled on the CA.

None of the above entry parameters have the "Depends On" field set.

![](images/IISUEntryParams.png)

Click Save to save the Certificate Store Type.

</details>
<details>
	<summary>SQL Server Extension</summary>

**In Keyfactor Command create a new Certificate Store Type as specified below:**

**Basic Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Name | WinSql | Display name for the store type (may be customized)
Short Name| WinSql | Short display name for the store type
Custom Capability | Leave Unchecked | Store type name orchestrator will register with. Check the box to allow entry of value
Supported Job Types | Inventory, Add, Remove | Job types the extension supports
Needs Server | Checked | Determines if a target server name is required when creating store
Blueprint Allowed | Unchecked | Determines if store type may be included in an Orchestrator blueprint
Uses PowerShell | Unchecked | Determines if underlying implementation is PowerShell
Requires Store Password	| Unchecked | Determines if a store password is required when configuring an individual store.
Supports Entry Password	| Unchecked | Determines if an individual entry within a store can have a password.

![](images/SQLServerCertStoreBasic.png)

**Advanced Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Store Path Type	| Fixed | Fixed to a defined path.  SQL Server Supports the Personal or "My" store on the Local Machine.
Store Path Value | My | Fixed Value My on the Local Machine Store.
Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias.
Private Keys | Required | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because SQL Server certificates without private keys would be useless.
PFX Password Style | Default or Custom | "Default" - PFX password is randomly generated, "Custom" - PFX password may be specified when the enrollment job is created (Requires the *Allow Custom Password* application setting to be enabled.)

![](images/SQLServerCertStoreAdvanced.png)

**Custom Fields:**

Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote
target server containing the certificate store to be managed

Name|Display Name|Type|Default Value / Options|Required|Description
---|---|---|---|---|---
WinRm Protocol|WinRm Protocol|Multiple Choice| https,http |Yes|Protocol that target server WinRM listener is using
WinRm Port|WinRm Port|String|5986|Yes| Port that target server WinRM listener is using. Typically 5985 for HTTP and 5986 for HTTPS
spnwithport|SPN With Port|Bool|false|No|Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.
ServerUsername|Server Username|Secret||No|The username to log into the target server (This field is automatically created).   Check the No Value Checkbox when using GMSA Accounts.
ServerPassword|Server Password|Secret||No|The password that matches the username to log into the target server (This field is automatically created).  Check the No Value Checkbox when using GMSA Accounts.
ServerUseSsl|Use SSL|Bool|true|Yes|Determine whether the server uses SSL or not (This field is automatically created)
RestartService|Restart SQL Service After Cert Installed|Bool|False|Yes|If true, Orchestrator will restart the SQL Server Service after installing the certificate.


*Note that some of the Names in the first column above have spaces and some do not, it is important to configure the Name field exactly as above.*


![](images/SQLServerCustomFields.png)

**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.

Name|Display Name| Type|Default Value|Required When|Description
---|---|---|---|---|---
InstanceName | Instance Name|String||Not required | When enrolling leave blank or use MSSQLServer for the Default Instance, Instance Name for an Instance or MSSQLServer,Instance Name if enrolling to multiple instances plus the default instance.

![](images/SQLServerEntryParams.png)

Click Save to save the Certificate Store Type.

</details>
<details>
	<summary>WinCert Extension</summary>

**1. In Keyfactor Command create a new Certificate Store Type using the settings below**

**Basic Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Name | Windows Certificate | Display name for the store type (may be customized)
Short Name| WinCert | Short display name for the store type
Custom Capability | WinCert | Store type name orchestrator will register with. Check the box to allow entry of value
Supported Job Types | Inventory, Add, Remove, Reenrollment | Job types the extension supports
Needs Server | Checked | Determines if a target server name is required when creating store
Blueprint Allowed | Unchecked | Determines if store type may be included in an Orchestrator blueprint
Uses PowerShell | Unchecked | Determines if underlying implementation is PowerShell
Requires Store Password	| Unchecked | Determines if a store password is required when configuring an individual store.
Supports Entry Password	| Unchecked | Determines if an individual entry within a store can have a password.

![](images/WinCertBasic.png)

**Advanced Settings:**

CONFIG ELEMENT | VALUE | DESCRIPTION
--|--|--
Store Path Type	| Freeform | Allows users to type in a valid certificate store.
Supports Custom Alias | Forbidden | Determines if an individual entry within a store can have a custom Alias.
Private Keys | Required | This determines if Keyfactor can send the private key associated with a certificate to the store. Required because IIS certificates without private keys would be useless.
PFX Password Style | Default or Custom | "Default" - PFX password is randomly generated, "Custom" - PFX password may be specified when the enrollment job is created (Requires the *Allow Custom Password* application setting to be enabled.)

![](images/WinCertAdvanced.png)

**Custom Fields:**

Custom fields operate at the certificate store level and are used to control how the orchestrator connects to the remote target server containing the certificate store to be managed

Name|Display Name|Type|Default Value / Options|Required|Description
---|---|---|---|---|---
WinRm Protocol|WinRm Protocol|Multiple Choice| https,http |Yes|Protocol that target server WinRM listener is using
WinRm Port|WinRm Port|String|5986|Yes| Port that target server WinRM listener is using. Typically 5985 for HTTP and 5986 for HTTPS
spnwithport|SPN With Port|Bool|false|No|Internally set the -IncludePortInSPN option when creating the remote PowerShell connection. Needed for some Kerberos configurations.
ServerUsername|Server Username|Secret||No|The username to log into the target server (This field is automatically created)
ServerPassword|Server Password|Secret||No|The password that matches the username to log into the target server (This field is automatically created)
ServerUseSsl|Use SSL|Bool|True|Yes|Determine whether the server uses SSL or not (This field is automatically created)

*Note that some of the Names in the first column above have spaces and some do not, it is important to configure the Name field exactly as above.*

![](images/WinCertCustom.png)

**Entry Parameters:**

Entry parameters are inventoried and maintained for each entry within a certificate store.
They are typically used to support binding of a certificate to a resource.
For the WinCert store type they are used to control how reenrollment jobs are performed.

Name|Display Name| Type|Default Value|Required When|Description
---|---|---|---|---|---
ProviderName | Crypto Provider Name | String ||| Name of the Windows cryptographic provider to use during reenrollment jobs when generating and storing the private keys. If not specified, defaults to 'Microsoft Strong Cryptographic Provider'. This value would typically be specified when leveraging a Hardware Security Module (HSM). The specified cryptographic provider must be available on the target server being managed. The list of installed cryptographic providers can be obtained by running 'certutil -csplist' on the target Server.
SAN | SAN | String || Reenrolling | Specifies Subject Alternative Name (SAN) to be used when performing reenrollment jobs. Certificate templates generally require a SAN that matches the subject of the certificate (per RFC 2818). Format is a list of <san_type>=<san_value> entries separated by ampersands. Examples: 'dns=www.mysite.com' for a single SAN or 'dns=www.mysite.com&dns=www.mysite2.com' for multiple SANs. Can be made optional if RFC 2818 is disabled on the CA.

None of the above entry parameters have the "Depends On" field set.

![](images/WinCertEntryParams.png)

Click Save to save the Certificate Store Type.

</details>


## Creating New Certificate Stores
Once the Certificate Store Types have been created, you need to create the Certificate Stores prior to using the extension.
Here are the settings required for each Store Type previously configured.

<details>
<summary>IISU Certificate Store</summary>

In Keyfactor Command, navigate to Certificate Stores from the Locations Menu.  Click the Add button to create a new Certificate Store using the settings defined below.

#### STORE CONFIGURATION 
CONFIG ELEMENT	|DESCRIPTION
----------------|---------------
Category | Select IIS Bound Certificate or the customized certificate store display name from above.
Container | Optional container to associate certificate store with.
Client Machine | Hostname of the Windows Server containing the certificate store to be managed. If this value is 'localhost', a local PowerShell runspace executing in the context of the Orchestrator service account will be used to access the certificate store and perform IIS binding operations. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields.
Store Path | Windows certificate store to manage. Choose "My" for the Personal Store or "WebHosting" for the Web Hosting Store. 
Orchestrator | Select an approved orchestrator capable of managing IIS Bound Certificates (one that has declared the IISU capability)
WinRm Protocol | Protocol to use when establishing the WinRM session. (Listener on Client Machine must be configured for selected protocol.)
WinRm Port | Port WinRM listener is configured for (HTTPS default is 5986)
SPN with Port | Typically False. Needed in some Kerberos configurations.
Server Username | Account to use when establishing the WinRM session to the Client Machine. Account needs to be an administrator or have been granted rights to manage IIS configuration and manipulate the local machine certificate store. If no account is specified, the security context of the Orchestrator service account will be used.
Server Password | Password to use when establishing the WinRM session to the Client Machine
Use SSL | Ignored for this certificate store type. Transport encryption is determined by the WinRM Protocol Setting
Inventory Schedule | The interval that the system will use to report on what certificates are currently in the store. 

![](images/IISUAddCertStore.png)

Click Save to save the settings for this Certificate Store
</details>

<details>
<summary>SQL Server Certificate Store</summary>

In Keyfactor Command, navigate to Certificate Stores from the Locations Menu.  Click the Add button to create a new Certificate Store using the settings defined below.

#### STORE CONFIGURATION 
CONFIG ELEMENT	|DESCRIPTION
----------------|---------------
Category | Select SQL Server Bound Certificate or the customized certificate store display name from above.
Container | Optional container to associate certificate store with.
Client Machine | Hostname of the Windows Server containing the certificate store to be managed. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields.
Store Path | Windows certificate store to manage. Fixed to "My". 
Orchestrator | Select an approved orchestrator capable of managing SQL Server Bound Certificates.
WinRm Protocol | Protocol to use when establishing the WinRM session. (Listener on Client Machine must be configured for selected protocol.)
WinRm Port | Port WinRM listener is configured for (HTTPS default is 5986)
SPN with Port | Typically False. Needed in some Kerberos configurations.
Server Username | Account to use when establishing the WinRM session to the Client Machine. Account needs to be an administrator or have been granted rights to manage IIS configuration and manipulate the local machine certificate store. If no account is specified, the security context of the Orchestrator service account will be used.
Server Password | Password to use when establishing the WinRM session to the Client Machine
Restart SQL Service After Cert Installed | For each instance the certificate is tied to, the service for that instance will be restarted after the certificate is successfully installed.
Use SSL | Ignored for this certificate store type. Transport encryption is determined by the WinRM Protocol Setting
Inventory Schedule | The interval that the system will use to report on what certificates are currently in the store. 

![](images/SQLServerAddCertStore.png)

Click Save to save the settings for this Certificate Store
</details>
<details>
<summary>WinCert Certificate Store</summary>
In Keyfactor Command, navigate to Certificate Stores from the Locations Menu.  Click the Add button to create a new Certificate Store using the settings defined below.


#### STORE CONFIGURATION 
CONFIG ELEMENT	|DESCRIPTION
----------------|---------------
Category | Select Windows Certificate or the customized certificate store display name from above.
Container | Optional container to associate certificate store with.
Client Machine | Hostname of the Windows Server containing the certificate store to be managed. If this value is 'localhost', a local PowerShell runspace executing in the context of the Orchestrator service account will be used to access the certificate store. If this value is a hostname, a WinRM session will be established using the credentials specified in the Server Username and Server Password fields.
Store Path | Windows certificate store to manage. Store must exist in the Local Machine store on the target server. 
Orchestrator | Select an approved orchestrator capable of managing Windows Certificates (one that has declared the WinCert capability)
WinRm Protocol | Protocol to use when establishing the WinRM session. (Listener on Client Machine must be configured for selected protocol.)
WinRm Port | Port WinRM listener is configured for (HTTPS default is 5986)
SPN with Port | Typically False. Needed in some Kerberos configurations.
Server Username | Account to use when establishing the WinRM session to the Client Machine. Account needs to be an admin or have been granted rights to manipulate the local machine certificate store. If no account is specified, the security context of the Orchestrator service account will be used.
Server Password | Password to use when establishing the WinRM session to the Client Machine
Use SSL | Ignored for this certificate store type. Transport encryption is determined by the WinRM Protocol Setting
Inventory Schedule | The interval that the system will use to report on what certificates are currently in the store. 

![](images/WinCertAddCertStore.png)

</details>



## Test Cases
<details>
<summary>IISU</summary>

Case Number|Case Name|Enrollment Params|Expected Results|Passed|Screenshot
----|------------------------|------------------------------------|--------------|----------------|-------------------------
1	|New Cert Enrollment To New Binding With KFSecret Creds|**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 0 - No SNI<br/>**Protocol:** https|New Binding Created with Enrollment Params specified creds pulled from KFSecret|True|![](images/TestCase1Results.gif)
2   |New Cert Enrollment To Existing Binding|**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 0 - No SNI<br/>**Protocol:** https|Existing Binding From Case 1 Updated with New Cert|True|![](images/TestCase2Results.gif)
3   |New Cert Enrollment To Existing Binding Enable SNI |**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Will Update Site In Case 2 to Have Sni Enabled|True|![](images/TestCase3Results.gif)
4   |New Cert Enrollment New IP Address|**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|New Binding Created With New IP and New SNI on Same Port|True|![](images/TestCase4Results.gif)
5   |New Cert Enrollment New Host Name|**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.newhostname.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|New Binding Created With different host on Same Port and IP Address|True|![](images/TestCase5Results.gif)
6   |New Cert Enrollment Same Site New Port |**Site Name:** FirstSite<br/>**Port:** 4443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.newhostname.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|New Binding on different port will be created with new cert enrolled|True|![](images/TestCase6Results.gif)
7   |Remove Cert and Binding From Test Case 6|**Site Name:** FirstSite<br/>**Port:** 4443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.newhostname.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert and Binding From Test Case 6 Removed|True|![](images/TestCase7Results.gif)
8   |Renew Same Cert on 2 Different Sites|`SITE 1`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https<br/>`SITE 2`<br/>**First Site**<br/>**Site Name:** SecondSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** cstiis04.cstpki.int<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert will be renewed on both sites because it has the same thumbprint|True|![](images/TestCase8Site1.gif)![](images/TestCase8Site2.gif)
9   |Renew Same Cert on Same Site Same Binding Settings Different Hostname|`BINDING 1`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https<br/>`BINDING 2`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsitebinding2.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert will be renewed on both bindings because it has the same thumbprint|True|![](images/TestCase9Binding1.gif)![](images/TestCase9Binding2.gif)
10  |Renew Single Cert on Same Site Same Binding Settings Different Hostname Different Certs|`BINDING 1`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https<br/>`BINDING 2`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsitebinding2.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert will be renewed on only one binding because the other binding does not match thumbprint|True|![](images/TestCase10Binding1.gif)![](images/TestCase10Binding2.gif)
11  |Renew Same Cert on Same Site Same Binding Settings Different IPs|`BINDING 1`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https<br/>`BINDING 2`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`192.168.58.160`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert will be renewed on both bindings because it has the same thumbprint|True|![](images/TestCase11Binding1.gif)![](images/TestCase11Binding2.gif)
12  |Renew Same Cert on Same Site Same Binding Settings Different Ports|`BINDING 1`<br/>**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https<br/>`BINDING 2`<br/>**Site Name:** FirstSite<br/>**Port:** 543<br/>**IP Address:**`192.168.58.162`<br/>**Host Name:** www.firstsitebinding1.com<br/>**Sni Flag:** 1 - SNI Enabled<br/>**Protocol:** https|Cert will be renewed on both bindings because it has the same thumbprint|True|![](images/TestCase12Binding1.gif)![](images/TestCase12Binding2.gif)
13	|ReEnrollment to Fortanix HSM|**Subject Name:** cn=www.mysite.com<br/>**Port:** 433<br/>**IP Address:**`*`<br/>**Host Name:** mysite.command.local<br/>**Site Name:**Default Web Site<br/>**Sni Flag:** 0 - No SNI<br/>**Protocol:** https<br/>**Provider Name:** Fortanix KMS CNG Provider<br/>**SAN:** dns=www.mysite.com&dns=mynewsite.com|Cert will be generated with keys stored in Fortanix HSM and the cert will be bound to the supplied site.|true|![](images/ReEnrollment1a.png)![](images/ReEnrollment1b.png)
14	|New Cert Enrollment To New Binding With Pam Creds|**Site Name:** FirstSite<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:** www.firstsite.com<br/>**Sni Flag:** 0 - No SNI<br/>**Protocol:** https|New Binding Created with Enrollment Params specified creds pulled from Pam Provider|True|![](images/TestCase1Results.gif)
15	|New Cert Enrollment Default Site No HostName|**Site Name:** Default Web Site<br/>**Port:** 443<br/>**IP Address:**`*`<br/>**Host Name:**<br/>**Sni Flag:** 0 - No SNI<br/>**Protocol:** https|New Binding Installed with no HostName|True|![](images/TestCase15Results.gif)
	
</details>
<details>
<summary>WinSql</summary>

Case Number|Case Name|Enrollment Params|Expected Results|Passed|Screenshot
----|------------------------|------------------------------------|--------------|----------------|-------------------------
1	|New Cert Enrollment To Default Instance Leave Blank|**Intance Name:** |Cert will be Installed to default Instance, Service will be restarted for default instance|True|![](images/SQLTestCase1.gif)
2	|New Cert Enrollment To Default Instance MSSQLServer|**Intance Name:** MSSQLServer|Cert will be Installed to default Instance, Service will be restarted for default instance|True|![](images/SQLTestCase2.gif)
3	|New Cert Enrollment To Instance1|**Intance Name:** Instance1|Cert will be Installed to Instance1, Service will be restarted for Instance1|True|![](images/SQLTestCase3.gif)
4	|New Cert Enrollment To Instance1 and Default Instance|**Intance Name:** MSSQLServer,Instance1|Cert will be Installed to Default Instance and Instance1, Service will be restarted for Default Instance and Instance1|True|![](images/SQLTestCase4.gif)
5	|One Click Renew Cert Enrollment To Instance1 and Default Instance|N/A|Cert will be Renewed/Installed to Default Instance and Instance1, Service will be restarted for Default Instance and Instance1|True|![](images/SQLTestCase5.gif)
6	|Remove Cert From Instance1 and Default Instance|**Intance Name:** |Cert from TC5 will be Removed From Default Instance and Instance1|True|![](images/SQLTestCase6.gif)
7	|Inventory Different Certs Different Instance|N/A|2 Certs will be inventoried and each tied to its Instance|True|![](images/SQLTestCase7.gif)
8	|Inventory Same Cert Different Instance|N/A|2 Certs will be inventoried the cert will have a comma separated list of Instances|True|![](images/SQLTestCase8.gif)
9	|Inventory Against Machine Without SQL Server|N/A|Will fail with error saying it can't find SQL Server|True|![](images/SQLTestCase9.gif)
	
</details>

