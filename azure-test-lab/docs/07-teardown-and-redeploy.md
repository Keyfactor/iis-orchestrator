# 7. Teardown and redeploy

## Never do this

```
az group delete -n rg-wincert-test-lab   # DO NOT RUN
```

`rg-wincert-test-lab` already existed before this lab was built into it and
may hold other resources you don't want touched. Every teardown approach
below is scoped to exactly what this lab created.

## Preferred: deployment stacks

If you deployed with `az stack group create` (recommended in
`docs/02-phase1-deploy.md` / `docs/04-phase2-deploy.md`), tear down in
**reverse order** - Phase 2 first, then Phase 1:

```
az stack group delete -g rg-wincert-test-lab -n wincert-lab-phase2 --action-on-unmanage deleteResources --yes
az stack group delete -g rg-wincert-test-lab -n wincert-lab-phase1 --action-on-unmanage deleteResources --yes
```

`--action-on-unmanage deleteResources` tells the stack to delete exactly the
resources it deployed, nothing else. This is the whole point of using
deployment stacks here - the stack itself is the record of what belongs to
this lab.

## Fallback: tag-based cleanup

If your `az cli` version doesn't support `az stack group` (check with
`az stack group create --help`; it needs a reasonably recent CLI), every
resource this lab creates is tagged `purpose=wincert-test-lab`. List them
before deleting anything, to confirm the scope:

```
az resource list -g rg-wincert-test-lab --tag purpose=wincert-test-lab -o table
```

Then delete them (VMs first, so their NICs/disks/public IPs detach cleanly,
then the remaining network resources):

```
az resource list -g rg-wincert-test-lab --tag purpose=wincert-test-lab --resource-type Microsoft.Compute/virtualMachines --query "[].id" -o tsv | xargs -n1 az resource delete --ids
az resource list -g rg-wincert-test-lab --tag purpose=wincert-test-lab --query "[].id" -o tsv | xargs -n1 az resource delete --ids
```

Review the output of the first `az resource list` carefully before running
any delete command - tag-based cleanup has no equivalent of a stack's
"this is exactly what I own" guarantee, so a mistagged or shared resource
could otherwise be caught up in it.

## Full rebuild from scratch

Useful after, say, a destructive `WinLDAP` Remove test against the live
domain controller that you want to back out of cleanly, or just to confirm
the whole lab still stands up correctly end to end:

1. `az stack group delete` for Phase 2, then Phase 1 (above).
2. Redeploy Phase 1 (`docs/02-phase1-deploy.md`).
3. Verify and redo the manual DNS cutover (`docs/03-manual-dns-cutover.md`) -
   this doesn't persist across a full teardown, since the VNet itself was
   deleted and recreated.
4. Redeploy Phase 2 (`docs/04-phase2-deploy.md`).
5. Re-run the verification runbook (`docs/06-verification-runbook.md`).
6. Reinstall the Universal Orchestrator + this extension on `dev-01` and
   re-point it at Keyfactor Command - this was always a manual step, so
   there's nothing Bicep-side to redo here, just your own install steps.
