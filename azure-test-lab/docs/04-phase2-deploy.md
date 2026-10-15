# 4. Phase 2: ADFS farm, IIS, SQL Server, and the dev box

Phase 2 deploys the five domain-joined member servers: `adfs-01`, `adfs-02`,
`iis-01`, `sql-01`, and `dev-01`. Do not run this until you've completed and
verified `docs/03-manual-dns-cutover.md`.

Every VM's NIC in Phase 2 is also given `dc-01`'s private IP as an explicit
DNS server (the `dcPrivateIp` parameter, default `10.20.1.4`) - this doesn't
rely on the VNet-level DNS cutover having "taken" on that specific NIC,
which matters if you're retrying a VM that was first created before the
cutover happened. If you changed `dcPrivateIp` away from the default in
Phase 1, pass the same value here too.

## Deploy

From `azure-test-lab/phase2-member-servers/`:

```
az deployment group create \
  --resource-group rg-wincert-test-lab \
  --name wincert-lab-phase2 \
  --template-file main.bicep \
  --parameters phase2.parameters.json \
  --parameters subnetId="<subnetId-from-phase1-outputs>" localAdminPassword="<same-password-as-phase1>" adfsServiceAccountPassword="<password>"
```

Or, using deployment stacks (recommended - see
`docs/07-teardown-and-redeploy.md`):

```
az stack group create \
  --resource-group rg-wincert-test-lab \
  --name wincert-lab-phase2 \
  --template-file main.bicep \
  --parameters phase2.parameters.json \
  --parameters subnetId="<subnetId-from-phase1-outputs>" localAdminPassword="<same-password-as-phase1>" adfsServiceAccountPassword="<password>" \
  --deny-settings-mode None
```

`localAdminPassword` **must** be the same password you used in Phase 1 -
`Install-ADDSForest` promotes the account that ran it into the domain's
built-in Administrator account, so that password is now your domain admin
password too. This is what lets the ADFS farm bootstrap scripts run with
domain-admin rights without you having to create and manage a second
credential.

## What happens, and how long it takes

1. A one-off run command on `dc-01` (`initialize-adfs-prereqs`) creates the
   `svc-adfs` service account, the DNS record for the federation service
   name, and a small file share used to hand the ADFS bootstrap certificate
   from `adfs-01` to `adfs-02`. A few minutes.
2. `adfs-01`, `iis-01`, `sql-01`, and `dev-01` are created and domain-joined
   in parallel.
3. `adfs-01`'s bootstrap script installs the ADFS role and creates the farm
   - this explicitly depends on step 1 having finished. 10-15 minutes.
4. `adfs-02` is created, domain-joined, and its bootstrap script joins the
   farm - this explicitly depends on `adfs-01`'s farm creation having
   finished (see `azure-test-lab/docs/05-adfs-farm-bootstrap.md` for the
   mechanics). 10-15 minutes.
5. `iis-01`'s bootstrap script installs IIS and binds a self-signed
   certificate to the Default Web Site. A few minutes.
6. `sql-01`'s bootstrap script confirms SQL Server is running and opens the
   firewall for 1433 (VNet-only). A few minutes.
7. `dev-01`'s bootstrap script installs Chocolatey, git, PowerShell 7, the
   .NET SDK, and Visual Studio 2022 Community. This is the slowest step -
   **20-40 minutes** depending on Visual Studio workload size, which is why
   its bootstrap timeout is set higher (90 minutes) than the other VMs'.

Total: expect **45-75 minutes** for the whole Phase 2 deployment, dominated
by the ADFS farm sequencing and the Visual Studio install.

## If something fails

Every bootstrap script in this lab is idempotent - if `az deployment group create`/`az stack group create` reports a failure partway through, fix the
underlying issue (check the run command's output/error via
`az vm run-command show`, or the specific troubleshooting notes in
`docs/05-adfs-farm-bootstrap.md`) and re-run the exact same deploy command.
Steps that already succeeded will no-op; only the failed step (and anything
downstream of it) will actually redo work.

## Next

Once Phase 2 succeeds, run through `docs/06-verification-runbook.md`,
"After Phase 2," before installing the Universal Orchestrator + this
extension on `dev-01` yourself.
