## Overview

The WinSql Certificate Store Type, referred to by its short name 'WinSql,' is designed for the management of certificates used by SQL Server instances. This store type allows users to automate the process of adding, removing, reenrolling, and inventorying certificates associated with SQL Server, thereby simplifying the management of SSL/TLS certificates for database servers.

### Caveats and Limitations

- **Caveats:** It's important to ensure that the Windows Remote Management (WinRM) is properly configured on the target server. The orchestrator relies on WinRM to perform its tasks, such as manipulating the Windows Certificate Stores. Misconfiguration of WinRM may lead to connection and permission issues.

- **Limitations:** Users should be aware that for this store type to function correctly, certain permissions are necessary. While some advanced users successfully use non-administrator accounts with specific permissions, it is officially supported only with Local Administrator permissions. Complexities with interactions between Group Policy, WinRM, User Account Control, and other environmental factors may impede operations if not properly configured.

### Verifying a Certificate Binding

After the orchestrator binds a certificate to a SQL Server instance, **SQL Server Configuration Manager (SSCM) may show an empty value in the Certificate dropdown** under SQL Server Network Configuration → Protocols → Properties → Certificate tab. This is a known display limitation of SSCM and does not indicate a problem with the binding. SSCM applies its own certificate eligibility filter when populating the dropdown and may exclude certificates that SQL Server itself loads and uses successfully, particularly certificates bound programmatically rather than through the SSCM UI.

Use the following two-step process to confirm a binding is correct independently of SSCM.

#### Step 1 — Confirm the thumbprint is written to the registry

Run the following on the SQL Server machine, replacing `MSSQLSERVER` with your instance name if using a named instance:

```powershell
$instance = "MSSQLSERVER"
$full = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance
(Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$full\MSSQLServer\SuperSocketNetLib" -Name "Certificate").ToUpper()
```

This should return the thumbprint of the bound certificate. If the value is empty, the binding was not written to the registry.

#### Step 2 — Confirm SQL Server loaded the certificate

After the SQL Server service restarts, it writes a confirmation to the SQL Server error log. Run the following to check:

```powershell
$logPath = (Resolve-Path "C:\Program Files\Microsoft SQL Server\MSSQL*\MSSQL\Log\ERRORLOG").Path
Select-String -Path $logPath -Pattern "certificate" -CaseSensitive:$false | ForEach-Object { $_.Line }
```

A successful binding produces a line similar to the following:

```
The certificate [Cert Hash(sha1) "D54E6CFFD7DF55FF9610355025BD603D7C25A2D4"] was successfully loaded for encryption.
```

The thumbprint in this message should match the value returned in Step 1. If the log instead shows `was not found or was not loaded`, the SQL Server service account does not have read access to the certificate's private key — contact your administrator to review private key permissions.

#### Note on `encrypt_option`

Binding a certificate does not automatically encrypt all client connections. The certificate is loaded and ready for use, but SQL Server will only negotiate TLS for a given connection when either the client requests it (`Encrypt=True` in the connection string) or the server is configured to force encryption. To verify that TLS is active for a specific connection, execute the following after connecting to the instance:

```sql
SELECT session_id, encrypt_option, net_transport
FROM sys.dm_exec_connections
WHERE session_id = @@SPID
```

`encrypt_option = TRUE` confirms TLS is in use for that connection. Whether to enforce encryption server-wide (Force Encryption setting in SSCM) is a separate operational decision outside the scope of the orchestrator.

