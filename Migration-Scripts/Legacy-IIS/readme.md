# Built-in IIS Certificate Store Type Migration Guide

As of Keyfactor Command v11, the built-in IIS certificate store types (IIS Personal, IIS Roots, and IIS Revoked) have been deprecated.
Before upgrading to v11, if you have existing built-in IIS certificate stores they should be migrated to one of the new IIS store types.
This guide will instruct you on how to migrate the built-in IIS certificate store types to the open source iis-orchestrator certificate store types: IISU and WinCert.

# Prerequisites

- Upgrade to Keyfactor Command >=10.4 (and < 11). This version or later is required to allow for the legacy IIS stores to be upgraded to both IISU and WinCert.
- All orchestrators that are currently being used to orchestrate the built-in IIS store types must support the certificate store type that the built-in stores are being migrated to (IISU, WinCert, or both).
- Ensure that you have a restorable database backup created before you begin the upgrade.

# Migration Scripts Usage

There are six SQL scripts that are needed for this migration:

<details>

<summary>Creation Scripts</summary>

<b>These scripts can be used to create the Store Type definitions, if they do not already exist.</b>
They may have already been created using `kfutil` or the Command portal.

## CreateIISUCertStoreType

[CreateIISUCertStoreType.sql](./CreateIISUCertStoreType.sql)
This script creates the IISU certificate store type.

## CreateWinCertStoreType

[CreateWinCertStoreType.sql](./CreateWinCertStoreType.sql)
This script creates the WinCert certificate store type.

</details>

<details>

<summary>Upgrade Scripts</summary>

## UpgradeIISRevokedAndRootsToWinCert

[UpgradeIISRevokedAndRootsToWinCert.sql](./UpgradeIISRevokedAndRootsToWinCert.sql)
This script creates a 'WinCert' certificate store copy for every 'IIS Revoked' and 'IIS Roots' certificate store. It will also create a 'WinCert' version of every 'IIS Revoked' and 'IIS Roots' certificate store container.

**Notes**

- This script does not delete the IIS certificate stores or containers.  
- By default, the orchestrator will use its service account credentials to connect to the new certificate stores. If other credentials should be used instead, they should be configured for each store from the Command Certificate Stores page.

This script accepts three parameters that allow configuration of WinRM:

| Parameter | Type | Valid Values | Default Value| Description |
|----|----|----|----|----|
|@winrm_protocol|NVARCHAR(5)|'https' or 'http'|https|The protocol that WinRM will use for interacting with the certificate stores|
|@winrm_port|INT|1 - 65535|5986|The port that WinRM will use for interacting with the certificate stores|
|@spnwithport|NVARCHAR(5)|'true' or 'false'|false|If set to 'true,' the `-IncludePortInSPN` flag will be set when WinRM creates the remote PowerShell connection|

## UpgradeIISPersonalToIISU

[UpgradeIISPersonalToIISU.sql](./UpgradeIISPersonalToIISU.sql)
This script creates an 'IISU' certificate store copy for a provided list of 'IIS Personal' certificate stores. It will also create an 'IISU' version of every 'IIS Personal' certificate store container.  

**Notes**

- This script does not delete the IIS certificate stores or containers.
- By default, the orchestrator will use its service account credentials to connect to the new certificate stores. If other credentials should be used instead, they should be configured for each store from the Command Certificate Stores page.


This script accepts four parameters:

| Parameter | Type | Valid Values | Default Value| Description |
|----|----|----|----|----|
|@comma_separated_store_ids|NVARCHAR(MAX)|* or a comma separated list of certificate store IDs. ex: 6A79C7A3-1A9B-413B-9C6E-571EA52B9E31,3929B040-299C-4EDF-98A4-EF0FFC21DAF9,C8A62749-10C3-4441-A0CC-AD83CA9051B5||A comma separated list of IDs of the IIS Personal certificate stores that you would like to migrate. If you would like to migrate all of the IIS Personal stores to this store type, you can provide '*' instead of a comma separated list.|
|@store_path|NVARCHAR(15)|'My' or 'WebHosting'|My|Used to select which Windows Certificate store that holds the IIS bound certificate. 'My' for the personal store, 'WebHosting' for the web hosting store|
|@winrm_protocol|NVARCHAR(5)|'https' or 'http'|https|The protocol that WinRM will use for interacting with the certificate stores|
|@winrm_port|INT|1 - 65535|5986|The port that WinRM will use for interacting with the certificate stores|
|@spnwithport|NVARCHAR(5)|'true' or 'false'|false|If set to 'true,' the `-IncludePortInSPN` flag will be set when WinRM creates the remote PowerShell connection|

## UpgradeIISPersonalToWinCert

[UpgradeIISPersonalToWinCert.sql](./UpgradeIISPersonalToWinCert.sql)
This script creates a 'WinCert' certificate store copy for a provided list of 'IIS Personal' certificate stores. It will also create a 'WinCert' version of every 'IIS Personal' certificate store container.

**Notes**

- This script does not delete the IIS certificate stores or containers.
- By default, the orchestrator will use its service account credentials to connect to the new certificate stores. If other credentials should be used instead, they should be configured for each store from the Command Certificate Stores page.

This script accepts four parameters:

| Parameter | Type | Valid Values | Default Value| Description |
|----|----|----|----|----|
|@comma_separated_store_ids|NVARCHAR(MAX)|* or a comma separated list of certificate store IDs. ex: 6A79C7A3-1A9B-413B-9C6E-571EA52B9E31,3929B040-299C-4EDF-98A4-EF0FFC21DAF9,C8A62749-10C3-4441-A0CC-AD83CA9051B5||A comma separated list of IDs of the IIS Personal certificate stores that you would like to migrate. If you would like to migrate all of the IIS Personal stores to this store type, you can provide '*' instead of a comma separated list.|
|@winrm_protocol|NVARCHAR(5)|'https' or 'http'|https|The protocol that WinRM will use for interacting with the certificate stores|
|@winrm_port|INT|1 - 65535|5986|The port that WinRM will use for interacting with the certificate stores|
|@spnwithport|NVARCHAR(5)|'true' or 'false'|false|If set to 'true,' the `-IncludePortInSPN` flag will be set when WinRM creates the remote PowerShell connection|

</details>

<details>

<summary>Deletion Scripts</summary>

## DeleteIISStores

[DeleteIISStores.sql](./DeleteIISStores.sql)
This script will delete all IIS Personal, IIS Roots, and IIS Revoked certificate store types, certificate stores, and certificate store containers.

</details>

# Migration Order

In order for a successful migration, the guides contained in this document should be completed in the following order:

1. If you have any IIS Roots or IIS Revoked stores, follow this guide: ['IIS Roots' and 'IIS Revoked' Migration Guide](#iis-roots-and-iis-revoked-migration-guide)
1. If you have any IIS Personal stores, follow this guide: [IIS Personal Migration Guide](#iis-personal-migration-guide)
1. Finally, Follow the [Migration Finalization Guide](#migration-finalization-guide)

## IIS Roots and IIS Revoked Migration Guide

<details>

<summary>Expand for steps</summary>

The 'IIS Roots' and 'IIS Revoked' certificate store types can only be migrated to the WinCert certificate store type.

1. Execute the `CreateWinCertStoreType` SQL script to define the WinCert certificate store type if you have not already created this type.
1. Execute the `UpgradeIISRevokedAndRootsToWinCert` script to create WinCert store copies of your 'IIS Roots' and 'IIS Revoked' stores. See [UpgradeIISRevokedAndRootsToWinCert section](#upgradeiisrevokedandrootstowincert) for usage information.

</details>

## IIS Personal Migration Guide

The IIS Personal stores can either be migrated to the WinCert store type, the IISU store type, or both, depending on your environment configuration. If you would like to manage all of the certificates in a server's 'Personal' certificate store, you should migrate the store to the WinCert type. If you would like to manage an IIS bound certificate on the server, you should migrate to the IISU store type.

### IIS Personal to WinCert Migration Guide

<details>

<summary>Expand for steps</summary>

1. If you have not already defined the WinCert store type, execute the `CreateWinCertStoreType` SQL script.
1. If you do not wish to create a WinCert store copy of all of your IIS Personal stores or have unique WinRM configurations for each certificate store, you should collect a list of IDs of the IIS Personal certificate stores that you wish to migrate at this time.
1. Execute the `UpgradeIISPersonalToWinCert` script to create WinCert store copies of your 'IIS Personal' stores. See [UpgradeIISPersonalToWinCert section](#upgradeiispersonaltowincert) for usage information.

</details>

### IIS Personal to IISU Migration Guide

<details>

<summary>Expand for steps</summary>

1. If you have not already defined the IISU store type, execute the `CreateIISUCertStoreType` SQL script.
1. If you do not wish to create aN IISU store copy of all of your IIS Personal stores or have unique configurations for each certificate store, you should collect a list of IDs of the IIS Personal certificate stores that you wish to migrate at this time.
1. Execute the `UpgradeIISPersonalToIISU` script to create WinCert store copies of your 'IIS Personal' stores. See [UpgradeIISPersonalToIISU section](#upgradeiispersonaltoiisu) for usage information.

</details>

## Migration Finalization Guide

At this point, you should have an IISU or WinCert store copy of every legacy IIS store type and store container. Follow these steps to complete the migration:

<details>

<summary>Expand for steps</summary>

1. Review the certificate stores that were created in the Command portal. Ensure that there is a copy of each legacy IIS store with the desired store type (either IISU or WinCert). If desired, wait for any scheduled inventory jobs to run on the new stores and ensure that they return the expected certificates.
1. Review the certificate store containers that were created in the Command portal. If you upgraded IIS Personal stores to both WinCert and IISU store types, all IIS Personal certificate store containers will have an IISU and a WinCert store type copy (they will have names that end in either ' - Upgraded WinCert' or ' - Upgraded IISU'). You will need to determine if you would like to keep the IISU or the WinCert copy of each container set then delete the one that you do not wish to keep. If you would like to keep both, you must rename one of the two. This is necessary, as the deletion step will remove the ' - Upgraded ...' text from the end of the store names; without updating the names, execution of the deletion script will result in invalid stores with the same name.
1. Execute the `DeleteIISStores` script to remove all legacy IIS stores, store types, and containers. This script will also remove the ' - Upgraded ...' text from the migrated container names.

</details>

At this point, all of your legacy IIS stores should be upgraded to either the IISU store type or the WinCert store type, and all legacy IIS stores, store types, containers, and jobs should be removed from Command.