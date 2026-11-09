# 1. Prerequisites

## Tools

- **Azure CLI** 2.60+ with the Bicep tooling installed:
  ```
  az bicep install
  az bicep upgrade
  ```
- Signed in to the right subscription/tenant:
  ```
  az login
  az account set --subscription "<your subscription name or ID>"
  ```
- Confirm the target resource group already exists (it should - this lab
  deploys **into** `rg-wincert-test-lab`, it never creates or deletes the
  resource group itself):
  ```
  az group show -n rg-wincert-test-lab
  ```

## SQL Server marketplace image

`sql-01` in Phase 2 uses the Azure Marketplace "SQL Server 2022 Developer on
Windows Server 2022" image (`sqldev-gen2`). This specific SKU is the free
Developer edition, so it does **not** need `az vm image terms accept` or a
marketplace purchase plan - `main.bicep` deliberately deploys it with no
`plan` block. If you ever swap in a paid SQL Server edition/SKU (Standard,
Enterprise), you'll need to both add the `plan` block back in
`phase2-member-servers/main.bicep` (`sqlMarketplacePlan`) and run
`az vm image terms accept --urn <publisher>:<offer>:<sku>:latest` once per
subscription before deploying.

## Find your public IP address

RDP access is locked down to a single IP address via the NSG. Find yours
with:

```
curl https://api.ipify.org
```

or by visiting `https://www.whatismyip.com` in a browser. You'll pass this
as `sourceIpAddress` when deploying Phase 1. If your IP changes later (e.g.
you're on a different network), update the NSG rule directly rather than
redeploying:

```
az network nsg rule update -g rg-wincert-test-lab --nsg-name nsg-wincert-lab \
  -n Allow-RDP-From-Admin-IP --source-address-prefixes "<new-ip>/32"
```

## Passwords

You'll need to choose (and remember, or store somewhere safe - e.g. a
password manager, not a file in this repo):

- A **local administrator / domain administrator** password (`localAdminPassword`,
  used in both phases - see `docs/02-phase1-deploy.md` for why this one
  password ends up serving both roles).
- A **Directory Services Restore Mode** password (`safeModeAdminPassword`,
  Phase 1 only).
- An **ADFS service account** password (`adfsServiceAccountPassword`,
  Phase 2 only).

Never commit real passwords into `phase1.parameters.json` or
`phase2.parameters.json` - those files are meant to hold placeholder text
and get supplied real values either interactively at deploy time
(`az deployment group create` will prompt for any parameter left out of the
file) or via a git-ignored `*.local.json` copy (see the root `.gitignore`).

## A note on cost

Every VM in this lab uses a cost-optimized burstable size and has a daily
auto-shutdown schedule (7:00 PM `Eastern Standard Time` by default - see the
`autoShutdownTime`/`timeZoneId` parameters). It's still 6 VMs running
concurrently (`dc-01`, `adfs-01`, `adfs-02`, `iis-01`, `sql-01`, `dev-01`), so
expect a non-trivial hourly cost while it's up. Tear it down
(`docs/07-teardown-and-redeploy.md`) when you're not actively using it,
rather than leaving it running 24/7 "just in case."
