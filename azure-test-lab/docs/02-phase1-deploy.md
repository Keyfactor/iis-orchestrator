# 2. Phase 1: VNet + Domain Controller

Phase 1 deploys the VNet, the NSG, and `dc-01`, then promotes `dc-01` into a
brand-new AD forest. It deploys nothing else - see
`azure-test-lab/README.md` for why the rest of the VMs wait for Phase 2.

## Deploy

From `azure-test-lab/phase1-foundation/`:

```
az deployment group create \
  --resource-group rg-wincert-test-lab \
  --name wincert-lab-phase1 \
  --template-file main.bicep \
  --parameters phase1.parameters.json \
  --parameters sourceIpAddress="<your-public-ip>" localAdminPassword="<password>" safeModeAdminPassword="<password>"
```

(Passing the two passwords directly on the command line keeps them out of
`phase1.parameters.json`; alternatively use a git-ignored `phase1.local.json`
that overrides them, or an Azure Key Vault reference - see
`docs/01-prerequisites.md`.)

Using **deployment stacks** instead is recommended so Phase 1 can be torn
down cleanly and independently later (see `docs/07-teardown-and-redeploy.md`
for why):

```
az stack group create \
  --resource-group rg-wincert-test-lab \
  --name wincert-lab-phase1 \
  --template-file main.bicep \
  --parameters phase1.parameters.json \
  --parameters sourceIpAddress="<your-public-ip>" localAdminPassword="<password>" safeModeAdminPassword="<password>" \
  --deny-settings-mode None
```

## What happens, and how long it takes

1. The NSG and VNet deploy in under a minute.
2. `dc-01` is created (a few minutes).
3. The `promote-ad-forest` run command executes `New-ADForest.ps1` on
   `dc-01`: installs the AD DS role, then `Install-ADDSForest`. This
   **reboots the VM** to complete promotion. Total time for this step is
   typically 10-15 minutes; the deployment waits for it since
   `treatFailureAsDeploymentFailure` is set.

The whole deployment usually finishes in 15-20 minutes.

## Important: do not skip verification before moving to the manual DNS cutover

Once the deployment reports success, verify `dc-01` actually finished
promoting **before** touching the VNet's DNS settings - see
`docs/06-verification-runbook.md`, "After Phase 1." If you cut over DNS
before the domain controller is actually ready, every Phase 2 VM will fail
to domain-join.

## Record the outputs

You'll need these for Phase 2 and the DNS cutover step:

```
az deployment group show -g rg-wincert-test-lab -n wincert-lab-phase1 --query properties.outputs
```

Specifically: `dcPrivateIpAddress` (for the DNS cutover in
`docs/03-manual-dns-cutover.md`) and `subnetId` (for Phase 2's `subnetId`
parameter).
