# WinCert Azure Test Lab

Bicep templates for a disposable, self-contained Azure test lab that
exercises every store type this repo's Universal Orchestrator extension
implements (`IISU/ImplementedStoreTypes/`): **WinIIS**, **WinCert**,
**WinSql**, **WinNetSH**, **WinLDAP**, and **WinAdfs**. Two of those -
**WinLDAP** and **WinAdfs** - have open questions in their own
documentation that only a real Domain Controller and a real ADFS farm can
settle; that's the main reason this lab exists.

## What gets built

A domain (`vmlab.local` by default) with six VMs, deployed into the
existing resource group `rg-wincert-test-lab`:

| VM | Role |
|---|---|
| `dc-01` | Domain Controller (new forest) |
| `adfs-01` | ADFS farm primary |
| `adfs-02` | ADFS farm secondary |
| `iis-01` | IIS server (WinIIS / WinNetSH / WinCert) |
| `sql-01` | SQL Server (WinSql) |
| `dev-01` | Domain-joined dev/debug box - Visual Studio, .NET SDK, PowerShell 7, git |

This is **infrastructure only**. Installing and configuring the real
Universal Orchestrator + this extension, and wiring it up to Keyfactor
Command, is a manual step you do yourself on `dev-01` once the network
exists - see the note at the bottom of this README. In the meantime, this
repo's own `WinCertTestConsole/` project plus Visual Studio on `dev-01`
already exercises the same store-type logic over WinRM without needing
Command at all.

## Deploy in order

This is a two-phase deployment with one manual step in between - see
"Why two phases?" below for the reasoning.

1. [`docs/01-prerequisites.md`](docs/01-prerequisites.md) - tools, marketplace
   terms acceptance, finding your IP, passwords, cost expectations.
2. [`docs/02-phase1-deploy.md`](docs/02-phase1-deploy.md) - deploy the VNet
   and `dc-01`, promote the AD forest.
3. [`docs/03-manual-dns-cutover.md`](docs/03-manual-dns-cutover.md) - point
   the VNet at `dc-01` for DNS. **Manual, on purpose.**
4. [`docs/04-phase2-deploy.md`](docs/04-phase2-deploy.md) - deploy the ADFS
   farm, IIS, SQL Server, and the dev box.
5. [`docs/05-adfs-farm-bootstrap.md`](docs/05-adfs-farm-bootstrap.md) - what
   the ADFS bootstrap scripts actually do, and the specific
   docs-vs-code question this lab is meant to help answer.
6. [`docs/06-verification-runbook.md`](docs/06-verification-runbook.md) -
   confirm each phase actually worked before moving on.
7. [`docs/07-teardown-and-redeploy.md`](docs/07-teardown-and-redeploy.md) -
   tear down safely (never `az group delete` on this resource group), or
   rebuild from scratch.

## Why two phases?

`dc-01` has to promote itself into a new AD forest (which requires a
reboot) before any other VM can domain-join against it, and the VNet's DNS
settings need to point at `dc-01` before those VMs are created - not after,
since an Azure NIC only picks up a VNet DNS change on its next DHCP lease.
Trying to encode "promote a DC, then flip DNS, then join five more VMs" into
a single nested Bicep deployment would work sometimes and fail confusingly
other times. Two explicit deployments with a five-minute manual command in
between is slower to type but much easier to debug when something goes
wrong - which, with a lab this size, it eventually will.

## Repo layout

```
azure-test-lab/
├── docs/                          # numbered runbook, read in order
├── modules/
│   ├── network.bicep              # VNet + subnet
│   ├── nsg.bicep                  # RDP from your IP only, everything else intra-VNet trusted
│   └── windows-vm.bicep           # generic VM: NIC, public IP, optional domain-join,
│                                  #   optional bootstrap runCommand, auto-shutdown
├── phase1-foundation/
│   ├── main.bicep
│   ├── phase1.parameters.json
│   └── scripts/New-ADForest.ps1
└── phase2-member-servers/
    ├── main.bicep
    ├── phase2.parameters.json
    └── scripts/
        ├── Initialize-AdfsPrereqs.ps1
        ├── Bootstrap-AdfsPrimary.ps1
        ├── Bootstrap-AdfsSecondary.ps1
        ├── Install-IISRole.ps1
        ├── Confirm-SqlServerReady.ps1
        └── Install-DevBoxPrereqs.ps1
```

## Security notes

- Remote access is **public IP per VM + NSG locked to your own IP**
  (`sourceIpAddress`) - RDP only, never opened to the internet at large.
  Traffic between VMs inside the VNet is trusted wholesale rather than
  port-by-port - a single AD domain needs far more than WinRM between its
  members (DNS, Kerberos, LDAP, SMB, RPC, ...), and every VM here is a
  member of the same single-subnet trust boundary anyway, so the
  meaningful security line is "VNet vs. internet," not inside the VNet.
- No HTTP/HTTPS/SQL/WinRM ports are exposed publicly; verification of
  IIS/SQL/WinRM happens either locally on-box after RDP in, or from
  another lab VM over the trusted intra-VNet path.
- Every VM uses Trusted Launch (Secure Boot + vTPM).
- No certificate authority is stood up for this lab - each role script
  bootstraps its own self-signed certificate, which is sufficient for every
  store type here since none of them depend on a trust chain. See
  `docs/05-adfs-farm-bootstrap.md` for the one certificate that matters
  most (the ADFS bootstrap cert) and why.
- Passwords are passed as `@secure()` Bicep parameters, never hardcoded.
  Sample parameter files use placeholder text - see
  `docs/01-prerequisites.md` for how to supply real values without
  committing them.

## After the lab is up: installing the orchestrator yourself

Once Phase 2 is deployed and verified, on `dev-01`:

1. Clone this repo, build it in Visual Studio.
2. Install the Universal Orchestrator and this extension per the main
   [README.md](../README.md), pointing it at your Keyfactor Command
   instance.
3. Or, for faster iteration without a full orchestrator install, use
   `WinCertTestConsole/` directly against any of the lab's WinRM targets.

Both of those are manual by design - this lab intentionally stops at
"infrastructure exists and is verified," not "Command is wired up."
