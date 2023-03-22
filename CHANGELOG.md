2.1.0
* Fixed issue that was occuring during renewal when there were bindings outside of http and https like net.tcp
* Added PAM registration/initialization documentation in README.md
* Resolved Null HostName error 
* Added WinCert Cert Store Type
* Added custom property parser to not show any passwords

2.0.0
* Add support for reenrollment jobs (On Device Key Generation) with the ability to specify a cryptographic provider. Specification of cryptographic provider allows HSM (Hardware Security Module) use.
* Local PAM Support added (requires Univesal Orchestrator Framework version 10.1)
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
