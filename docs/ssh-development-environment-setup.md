# Setting Up a Dev/Test Environment for SSH (Linux Orchestrator → Windows Target)

This guide walks a developer through standing up a local test rig that mirrors how the
WinCert/IISU/WinSQL extensions connect from a **Linux-hosted Universal Orchestrator** to a
**Windows target server** over SSH, so you can develop and debug the SSH code path
(`PSHelper.InitializeRemoteSession`, protocol `ssh`) without needing a full Keyfactor Command
instance.

It assumes you've already read the "Using the WinCert Extension on Linux servers" section of
[docsource/content.md](../docsource/content.md); this document is the practical, step-by-step
companion to that overview.

## 1. How the SSH connection actually works

This extension does **not** shell out to `ssh` and run scripts remotely. It uses **PowerShell
remoting over an SSH transport** (`New-PSSession -HostName ... -KeyFilePath ...`), which is a
PSRP (PowerShell Remoting Protocol) session tunneled through the OpenSSH connection. That means
both sides need more than just an SSH server/client — they need PowerShell 6+ wired into SSH as a
subsystem. See [IISU/PSHelper.cs](../IISU/PSHelper.cs), `InitializeRemoteSession()`:

```csharp
Hashtable options = new Hashtable
{
    { "StrictHostKeyChecking", "No" },
    { "UserKnownHostsFile", "/dev/null" },
};

PS.AddCommand("New-PSSession")
    .AddParameter("HostName", ClientMachineName)
    .AddParameter("UserName", serverUserName)
    .AddParameter("KeyFilePath", tempKeyFilePath)
    .AddParameter("ConnectingTimeout", 10000)
    .AddParameter("Options", options);
```

A few things fall out of this that matter for your dev setup:

* **Host key checking is disabled** (`StrictHostKeyChecking = No`, known-hosts file is
  `/dev/null`). This is convenient for a throwaway lab VM but is a real security consideration —
  don't assume the same is safe against a production target.
* **The private key comes from the certificate store's Password field, not a file on disk.**
  `PSHelper.createPrivateKeyFile()` writes whatever string is in the "Server Password" field to a
  temp file (`chmod 600` on Linux, ACL-restricted on Windows) and hands that path to
  `-KeyFilePath`. So when you configure a store (or a test harness) for SSH, the "password" you
  provide must be the **full PEM private key contents**, not a passphrase.
* **The key must not be passphrase-protected.** Nothing in the code prompts for a passphrase; a
  protected key will hang the session on the interactive `ssh` prompt.
* This all runs from inside the orchestrator process using the PowerShell SDK
  (`System.Management.Automation`), which in turn invokes the local `ssh` binary as a
  subprocess — so the orchestrator host needs a working `ssh` client, not just the PowerShell SDK.

## 2. What you need on each machine

| Machine            | Role              | Requirements                                                                                                                                                              |
| ------------------ | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Linux dev/test box | Orchestrator host | PowerShell 7.4+ (`pwsh`), OpenSSH client (`ssh`)                                                                                                                      |
| Windows target     | Managed server    | PowerShell 6+ (7.4+ recommended), OpenSSH**Server**, `sshd` configured with a PowerShell subsystem, WebAdministration/IISAdministration modules if testing WinIIS |

The full breakdown of hardware and software minimums for each side is in Section 3.

## 3. Minimum machine and software requirements

There is no separate published hardware spec for this extension — it inherits whatever the
Universal Orchestrator process and PowerShell SDK need, plus a comfortable margin for a lab VM.
The numbers below are practical minimums for a dev/test rig, not an official Keyfactor capacity
plan; size a production orchestrator/target per your own workload and Keyfactor's Universal
Orchestrator documentation.

### 3.1 Ubuntu dev/test box (orchestrator host)

|              | Minimum                                                                                                          | Notes                                                                                                                                                                                                                                |
| ------------ | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| OS           | Ubuntu 20.04 LTS                                                                                                 | Any release still on .NET's[supported Linux distros list](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md) for the .NET version you run works; 22.04/24.04 LTS are fine and recommended for a new lab box. |
| CPU          | 2 vCPU                                                                                                           | The orchestrator itself is not CPU-heavy; this just keeps`dotnet`/`pwsh` startup and job execution responsive.                                                                                                                   |
| Memory       | 2 GB RAM                                                                                                         | 4 GB is more comfortable if you're also running the IDE, Docker, or multiple concurrent test sessions on the same box.                                                                                                               |
| Disk         | 10 GB free                                                                                                       | Covers the .NET runtime, PowerShell, the orchestrator deployment package, and SSH host/known-hosts state. Add headroom if you're also building the repo locally (`bin`/`obj` output, NuGet cache).                               |
| Network      | Outbound TCP 22 to the Windows target; outbound HTTPS to Keyfactor Command (if registering with a real instance) |                                                                                                                                                                                                                                      |
| .NET runtime | .NET 8 or .NET 10                                                                                                | Matches the`TargetFrameworks` in [IISU/IISU.csproj](../IISU/IISU.csproj) (`net8.0;net10.0`). Whatever the Universal Orchestrator process is built against is what actually needs to be installed.                                 |
| PowerShell   | `pwsh` 7.4+                                                                                                    | Must match (or be compatible with) the`Microsoft.PowerShell.SDK` version referenced in the `.csproj` (7.4.15 for net8.0, 7.6.1 for net10.0) — see [IISU/IISU.csproj](../IISU/IISU.csproj).                                       |
| SSH          | OpenSSH client (`openssh-client`/`openssh-clients` package)                                                  | Required because PowerShell's SSH transport shells out to the local`ssh` binary.                                                                                                                                                   |

### 3.2 Windows Server target (managed server)

|                                  | Minimum                                                                                                          | Notes                                                                                                                                                                                                                                                                                                           |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OS                               | Windows Server 2016                                                                                              | Anything earlier (2012 R2) technically runs WMF 5.1 but is past Microsoft mainstream support and is not a realistic dev target. 2019/2022/2025 are all fine; the[SSL flags table in docsource/iisu.md](../docsource/iisu.md) shows which IIS/SNI features vary by version if you're testing WinIIS specifically. |
| CPU                              | 2 vCPU                                                                                                           |                                                                                                                                                                                                                                                                                                                 |
| Memory                           | 4 GB RAM                                                                                                         | Windows Server itself is the floor here, not this extension. 4 GB is a comfortable minimum for Server Core; add more if you're also running full Desktop Experience, SQL Server (for WinSQL testing), or ADFS.                                                                                                  |
| Disk                             | 40 GB free                                                                                                       | Covers the Windows Server OS, PowerShell 7.x, OpenSSH Server, and IIS if applicable. Windows Server's own install footprint dominates this number, not the extension.                                                                                                                                           |
| Network                          | Inbound TCP 22 (SSH); TCP 5985/5986 instead if you're testing the WinRM path                                     | Firewall rule is usually added automatically by the OpenSSH.Server capability, but verify it.                                                                                                                                                                                                                   |
| PowerShell                       | Windows PowerShell 5.1 (included with Server 2016+)**and** PowerShell 7.4+ (`pwsh`) installed separately | 5.1 is what's present out of the box;**7.x must be installed explicitly** and wired in as the SSH subsystem — Windows PowerShell 5.1 cannot serve as an SSH subsystem host for this transport. See [README.md](../README.md) (Prerequisites) for the general PS version guidance.                         |
| SSH                              | OpenSSH**Server** optional feature (`OpenSSH.Server`), `sshd` service running                          | See Section 4 below for the subsystem wiring.                                                                                                                                                                                                                                                                   |
| IIS (WinIIS testing only)        | IIS role installed,`WebAdministration` and `IISAdministration` PowerShell modules v1.1                       | Only needed if you're exercising the WinIIS store type; skip for WinCert/WinSQL-only testing. Per[docsource/content.md](../docsource/content.md), these modules must be present and are not something this extension installs for you.                                                                           |
| SQL Server (WinSQL testing only) | Any supported SQL Server version with a certificate bound for SSL                                                | Only needed for WinSQL testing.                                                                                                                                                                                                                                                                                 |
| ADFS (WinADFS testing only)      | ADFS role installed, ADFS PowerShell module available                                                            | Only needed for WinADFS testing; note ADFS stores don't support JEA or run via SSH — they're WinRM/agent-mode only.                                                                                                                                                                                            |

## 4. Configure the Windows target to accept PowerShell-over-SSH

Run these on the Windows box you intend to manage (a VM is fine).

1. **Install PowerShell 7.x** (if not already present):

   ```powershell
   winget install --id Microsoft.PowerShell --source winget
   ```
2. **Install the OpenSSH Server optional feature** and start the service:

   ```powershell
   Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
   Start-Service sshd
   Set-Service -Name sshd -StartupType Automatic
   ```
3. **Register PowerShell as an SSH subsystem.** Edit
   `C:\ProgramData\ssh\sshd_config` and add (adjust the path to match your installed PowerShell
   version):

   ```text
   Subsystem powershell "C:\Program Files\PowerShell\7\pwsh.exe" -sshs -NoLogo -NoProfile
   ```
4. **Add the dev box's public key.** Generate a keypair on the *Linux* box first (Section 5,
   step 2), then copy the **public** key here.

   * If the connecting account is a member of `Administrators`, the key must go in
     `C:\ProgramData\ssh\administrators_authorized_keys` (not the user's own `.ssh` folder).
   * That file's ACL must be restricted to `Administrators` and `SYSTEM` only, or `sshd` will
     silently ignore it:

     ```powershell
     icacls "C:\ProgramData\ssh\administrators_authorized_keys" /inheritance:r
     icacls "C:\ProgramData\ssh\administrators_authorized_keys" /grant "Administrators:F" "SYSTEM:F"
     ```
5. **Restart sshd** so the subsystem and key changes take effect:

   ```powershell
   Restart-Service sshd
   ```
6. **Open the firewall** for port 22 if it isn't already (the OpenSSH.Server feature usually adds
   this rule automatically — verify with `Get-NetFirewallRule -Name *ssh*`).

## 5. Configure the Linux dev box as the "orchestrator"

1. **Install PowerShell 7.4+** and the OpenSSH client. Example for RHEL/Fedora-family:

   ```bash
   sudo dnf install https://github.com/PowerShell/PowerShell/releases/download/v7.5.2/powershell-7.5.2-1.rh.x86_64.rpm
   sudo dnf install openssh-clients openssl
   ```

   (Debian/Ubuntu: follow Microsoft's `packages-microsoft-prod` apt instructions for `powershell`,
   then `sudo apt install openssh-client`.)
2. **Generate a passphrase-less keypair** dedicated to this test rig (do not reuse a personal key):

   ```bash
   ssh-keygen -t ed25519 -f ~/.ssh/kf_wincert_test -N ""
   ```
3. **Copy the public key to the Windows target** as described in Section 4, step 4.
4. **Sanity-check raw SSH first**, before involving PowerShell remoting at all:

   ```bash
   ssh -i ~/.ssh/kf_wincert_test winuser@<windows-target> "pwsh -v"
   ```

   If this doesn't return a PowerShell version string, fix SSH/subsystem config before going any
   further — the extension's session will fail the same way.
5. **Then confirm PowerShell remoting over SSH works** end-to-end:

   ```bash
   pwsh -Command '
     $s = New-PSSession -HostName <windows-target> -UserName winuser -KeyFilePath ~/.ssh/kf_wincert_test
     Invoke-Command -Session $s -ScriptBlock { $PSVersionTable; whoami }
     Remove-PSSession $s
   '
   ```

   This is the exact call `PSHelper.InitializeRemoteSession()` makes internally, so if it works
   here, the extension's SSH path will work too.

## 6. Running the extension itself against this rig

Once steps 3–4 succeed, you can point the extension at the target in two ways:

### Option A — Integration test project (fastest feedback loop)

[WindowsCertStore.IntegrationTests](../WindowsCertStore.IntegrationTests) already has the
plumbing to run real jobs against a real server:

1. Add an entry to
   [WindowsCertStore.IntegrationTests/servers.json](../WindowsCertStore.IntegrationTests/servers.json)
   for your target, e.g.:

   ```json
   { "Machine": "<windows-target>", "StoreType": "WinCert", "JEAEndpointName": "" }
   ```
2. Supply credentials via environment variables — `Username` is your SSH user, `Password` (here
   named `PrivateKey` in code) is the **full contents of the private key file**:

   ```bash
   export KEYFACTOR_TEST_USER=winuser
   export KEYFACTOR_TEST_PASSWORD="$(cat ~/.ssh/kf_wincert_test)"
   ```

   (Alternatively populate `appsettings.json` with an Azure Key Vault URI — see
   [VaultHelper.cs](../WindowsCertStore.IntegrationTests/VaultHelper.cs) — if your team keeps test
   creds in Key Vault instead of env vars.)
3. The certificate-store properties built in
   [WinCertIntegrationTests.cs](../WindowsCertStore.IntegrationTests/WinCertIntegrationTests.cs)
   (`GetCertStoreJobProperties`) currently hardcode `"WinRm Protocol": "http"`. For an SSH run,
   change that to:

   ```csharp
   ["WinRm Protocol"] = "ssh",
   ["WinRm Port"] = "22",
   ```
4. Run just the integration tests:

   ```bash
   dotnet test WindowsCertStore.IntegrationTests --filter Category=Integration
   ```

### Option B — Deploy against a real Keyfactor Command instance

Configure a certificate store (WinCert/WinIIS/WinSQL) with:

* **WinRM Protocol**: `ssh`
* **WinRM Port**: `22` (default for ssh; see `integration-manifest.json`)
* **Client Machine**: the target's hostname or IP (do **not** use `|LocalMachine` — SSH always
  requires a real remote connection)
* **Server Username**: the SSH-authorized account
* **Server Password**: the full PEM private key contents (headers included), unencrypted

Run an Inventory job and check the orchestrator log — `PSHelper` logs each step at `Trace`/`Debug`
level (session creation, script loading, errors), which is the fastest way to see where an SSH
connection is failing.

## 7. Testing inside a container

If you're testing the Docker deployment path described in
[docsource/content.md](../docsource/content.md), the container needs the same three ingredients
as the bare-metal Linux box — PowerShell, an SSH client, and (for WinRM-based stores) PSWSMan:

```text
dnf install https://github.com/PowerShell/PowerShell/releases/download/v7.5.2/powershell-7.5.2-1.rh.x86_64.rpm
pwsh -Command 'Install-Module -Name PSWSMan'
dnf install openssh-clients openssl
```

## 8. Troubleshooting

| Symptom                                                                                                | Likely cause                                                                                                                                                                                                         |
| ------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ssh -i key user@target "pwsh -v"` hangs or asks for a passphrase                                    | Key has a passphrase — regenerate without one (Section 5, step 2).                                                                                                                                                  |
| `subsystem request failed` / connection closes immediately after auth                                | The`Subsystem powershell ...` line is missing, misspelled, or points to a `pwsh.exe` path that doesn't exist on the target. Re-check Section 4, step 3, then `Restart-Service sshd`.                           |
| SSH key auth works for a normal user but fails for an admin account                                    | Admin accounts must have their key in`administrators_authorized_keys`, not `.ssh/authorized_keys`, and that file's ACL must be locked down to `Administrators`+`SYSTEM` (Section 4, step 4).                 |
| `New-PSSession -HostName ...` fails with a generic WinRM-sounding error even though `protocol=ssh` | Don't be misled by the error text — this is still the SSH transport. Re-run the raw`ssh` test (Section 5, step 4) to isolate whether it's an SSH problem or a PSRP/subsystem problem.                             |
| Extension throws`TimeoutException` after ~10s (or 30s at the `PSHelper` level)                     | Network/firewall issue, or`sshd` isn't listening — verify with `Test-NetConnection <target> -Port 22` from the Linux box and confirm the Windows firewall rule for OpenSSH is enabled.                          |
| Works with raw`ssh`/`pwsh` from your shell but fails when run by the orchestrator service          | Check which account is actually running the Universal Orchestrator process — the SSH client config and any`known_hosts`/key files must be reachable by *that* account, not just your interactive login.         |
| `PS.HadErrors` / script load errors after the session connects                                       | The scripts directory (`PowerShell/`) wasn't found or a `.ps1` file failed to parse remotely — see `PSHelper.FindScriptsDirectory` / `LoadAllScripts` and check the orchestrator log for the specific file. |

## 9. Related files

* [IISU/PSHelper.cs](../IISU/PSHelper.cs) — connection logic for both SSH and WinRM (`InitializeRemoteSession`, `createPrivateKeyFile`).
* [docsource/content.md](../docsource/content.md) — customer-facing requirements text this guide expands on.
* [integration-manifest.json](../integration-manifest.json) — authoritative list of store parameters (`WinRM Protocol`, `WinRM Port`, `ServerUsername`, `ServerPassword`, `JEAEndpointName`).
* [WindowsCertStore.IntegrationTests/](../WindowsCertStore.IntegrationTests) — real end-to-end test harness (`servers.json`, `ConnectionFactory.cs`).
* [WinRmTroubleshooting.ps1](../WinRmTroubleshooting.ps1) — the WinRM equivalent of this guide, useful if you're comparing the two transports side by side.

Note: JEA (Just Enough Administration) is **not** supported over SSH — see the JEA section of
[docsource/content.md](../docsource/content.md) for that (WinRM-only) setup path.
