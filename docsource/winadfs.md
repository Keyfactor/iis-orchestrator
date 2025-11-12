## Overview

WinADFS is a store type designed for managing certificates within Microsoft Active Directory Federation Services (ADFS) environments. This store type enables users to automate the management of certificates used for securing ADFS communications, including tasks such as adding, removing, and renewing certificates associated with ADFS services.
* NOTE: Only the Service-Communications certificate is currently supported.  Follow your ADFS best practices for token encrypt and decrypt certificate management.
* NOTE: This extension also supports the auto-removal of expired certificates from the ADFS stores on the Primary and Secondary nodes during the certificate rotation process, along with restarting the ADFS service to apply changes.

## Requirements

When using WinADFS, the Universal Orchestrator must act as an agent and be installed on the Primary ADFS server within the ADFS farm. This is necessary because ADFS configurations and certificate management operations must be performed directly on the ADFS server itself to ensure proper functionality and security.

## Certificate Store Configuration

When creating a Certificate Store for WinADFS, the Client Machine name must be set as an agent and use the LocalMachine moniker, for example: myADFSPrimary|LocalMachine.

