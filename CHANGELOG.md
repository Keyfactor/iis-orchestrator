2.4.0
* Changed the way certificates are added to cert stores.  CertUtil is now used to import the PFX certificate into the associated store.  The CSP is now considered when maintaining certificates, empty CSP values will result in using the machines default CSP.
* Added the Crypto Service Provider and SAN Entry Parameters to be used on Inventory queries, Adding and ReEnrollments for the WinCert and IISU extensions.  The CSP was added for WinSWL for Management Add jobs only.

2.3.1
* Added additional error trapping for WinRM connections to allow actual error on failure.

2.3.0
* Added Sql Server Binding Support
* Modified WinCert Advanced PrivateKeyAllowed setting from Required to Optional
  
2.2.2
* Removed empty constructor to resolve PAM provider error when using WinCert store types

2.2.1
* Fixed issue where https binding without cert was causing an error
  
2.2.0
* Added Support for GMSA Account by using no value for ServerUsernanme and ServerPassword. KF Command version 10.2 or later is required to specify empty credentials. 
* Added local PowerShell support, triggered when specifying 'localhost' as the client machine while using the IISU or WinCert Orchestrator.  This change was tested using KF Command 10.3
* Moved to .NET 6

2.1.1
* Fixed the missing site name error when issuing a WinCert job when writing trace log settings to the log file.
* Several display names changed in the documented certificate store type definitions. There are no changes to the internal type or parameter names, so no migration is necessary for currently configured stores.
	* Display name for IISU changed to "IIS Bound Certificate".
	* Display name for WinCert changed to "Windows Certificate".
	* Display names for several Store and Entry parameters changed to be more descriptive and UI friendly.
* Significant readme cleanup

2.1.0
* Fixed issue that was occurring during renewal when there were bindings outside of http and https like net.tcp
* Added PAM registration/initialization documentation in README.md
* Resolved Null HostName error 
* Added WinCert Cert Store Type
* Added custom property parser to not show any passwords
* Removed any password references in trace logs and output settings in JSON format

2.0.0
* Add support for reenrollment jobs (On Device Key Generation) with the ability to specify a cryptographic provider. Specification of cryptographic provider allows HSM (Hardware Security Module) use.
* Local PAM Support added (requires Universal Orchestrator Framework version 10.1)
* Certificate store type changed from IISBin to IISU. See readme for migration notes.


1.1.3
* Made WinRM port a store parameter
* Made WinRM protocol a store parameter
* IISWBin 1.1.3 upgrade script.sql added to upgrade from 1.1.2

1.1.0
* Migrate to Universal Orchestrator (KF9 / .NET Core)
* Perform Renewals using RenewalThumbprint

1.0.3
* Add support for the SNI Flags when creating new bindings.  Supported flags include:
	* 0  No SNI
    * 1  SNI Enabled
    * 2  Non SNI binding which uses Central Certificate Store
    * 3  SNI binding which uses Central Certificate Store
* Last release to support Windows Orchestrator (KF8)

1.0.2
* Remove dependence on Windows.Web.Administration on the orchestrator server.  The agent will now use the local version on the managed server via remote powershell
* add support for the IncludePortInSPN flag
* add support to use credentials from Keyfactor for Add/Remove/Inventory jobs.  
