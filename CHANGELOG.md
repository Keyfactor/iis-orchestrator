2.6.1
* Documentation updates for the 2.6 release
* Fix a naming typo in the 2.5 migration SQL script
* Update integration-manifest.json
* Updated the Alias in IIS to also include Site-Name.  NOTE: Inventory will need to be performed prior to any management job to include new Alias format.
* Added Bindings check when attempting to add bindings that already exist or are ambiguous.  NOTE:  If you wish to add multiple bindings with the same IP:Port, Hostname must be included and SNI flag must be set to a minimum of '1'. Failure to do this can result in failed jobs with a binding conflict error message.
* Bumped Keyfactor.Orchestrator.Common to 3.2.0 to correct signing issue.
* Bumped System.IO.Packaging to 6.0.2 & 8.0.1 for .Net vulnerabilities.

2.6.0
* Added the ability to run the extension in a Linux environment.  To utilize this change, for each Cert Store Types (WinCert/WinIIS/WinSQL), add ssh to the Custom Field <b>WinRM Protocol</b>.  When using ssh as a protocol, make sure to enter the appropriate ssh port number under WinRM Port.
* NOTE: For legacy purposes the Display names WinRM Protocol and WinRM Port are maintained although the type of protocols now includes ssh.
* Moved all inventory and management jobs to external PowerShell script file .\PowerShellScripts\WinCertScripts.ps1
* NOTE:  This version was not publicly released.

2.5.1
* Fixed WinSQL service name when InstanceID differs from InstanceName

2.5.0
* Added the Bindings to the end of the thumbprint to make the alias unique.
* Using new IISWebBindings cmdlet to use additional SSL flags when binding certificate to website.
* Added multi-platform support for .Net6 and .Net8.
* Updated various PowerShell scripts to handle both .Net6 and .Net8 differences (specifically the absence of the WebAdministration module in PS SDK 7.4.x+)
* Fixed issue to update multiple websites when using the same cert.
* Removed renewal thumbprint logic to update multiple website; each job now updates its own specific certificate.

2.4.4
* Fix an issue with WinRM parameters when migrating Legacy IIS Stores to the WinCert type
* Fix an issue with "Delete" script in the Legacy IIS Migration that did not remove some records from dependent tables

2.4.3
* Adding Legacy IIS Migration scripting and ReadMe guide

2.4.2
* Correct false positive error when completing an IIS inventory job.
* Revert to specifying the version of PowerShell to use when establishing a local PowerShell Runspace.
* Fixed typo in error message.

2.4.1
* Modified the CertUtil logic to use the -addstore argument when no password is sent with the certificate information.
* Added additional error trapping and trace logs

2.4.0
* Changed the way certificates are added to cert stores.  CertUtil is now used to import the PFX certificate into the associated store.  The CSP is now considered when maintaining certificates, empty CSP values will result in using the machines default CSP.
* Added the Crypto Service Provider and SAN Entry Parameters to be used on Inventory queries, Adding and ReEnrollments for the WinCert, WinSQL and IISU extensions.
* Changed how Client Machine Names are handled when a 'localhost' connection is desired.  The new naming convention is:  {machineName}|localmachine.  This will eliminate the issue of unique naming conflicts.
* Updated the manifest.json to now include WinSQL ReEnrollment.
* Updated the integration-manifest.json file for new fields in cert store types.

2.3.2
* Changed the Open Cert Store access level from a '5' to 'MaxAllowed'

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
* Significant ReadMe cleanup

2.1.0
* Fixed issue that was occurring during renewal when there were bindings outside of http and https like net.tcp
* Added PAM registration/initialization documentation in README.md
* Resolved Null HostName error 
* Added WinCert Cert Store Type
* Added custom property parser to not show any passwords
* Removed any password references in trace logs and output settings in JSON format

2.0.0
* Add support for re-enrollment jobs (On Device Key Generation) with the ability to specify a cryptographic provider. Specification of cryptographic provider allows HSM (Hardware Security Module) use.
* Local PAM Support added (requires Universal Orchestrator Framework version 10.1)
* Certificate store type changed from IISBin to IISU. See README for migration notes.


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
* Remove dependence on Windows.Web.Administration on the orchestrator server.  The agent will now use the local version on the managed server via remote PowerShell
* add support for the IncludePortInSPN flag
* add support to use credentials from Keyfactor for Add/Remove/Inventory jobs.  
