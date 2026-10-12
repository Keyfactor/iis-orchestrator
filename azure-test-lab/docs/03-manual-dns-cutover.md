# 3. Manual step: point the VNet's DNS at the domain controller

This is the one step in the whole lab that is deliberately **not**
automated. It's a short, safe, single command - but it needs a human to run
it at the right moment, after confirming Phase 1 actually succeeded (see
`docs/06-verification-runbook.md`, "After Phase 1").

## Why this is manual, and why it must happen before Phase 2

Every VM in this lab needs to resolve domain names through `dc-01`'s DNS
server (installed alongside AD DS) in order to find the domain and join it.
The VNet itself doesn't know about `dc-01` until you tell it to use it as a
custom DNS server.

Doing this *before* any Phase 2 VM exists means each of them picks up the
right DNS server automatically the first time their NIC gets a DHCP lease
(i.e., on creation) - no per-VM DNS override is needed. If you did this
*after* Phase 2 VMs already existed, you'd need to reboot each of their NICs
to pick up the change, which is exactly the kind of fragile, easy-to-forget
step this lab's phasing is designed to avoid.

## Run this

```
az network vnet update \
  --resource-group rg-wincert-test-lab \
  --name vnet-wincert-lab \
  --dns-servers <dcPrivateIpAddress-from-phase1-outputs>
```

`<dcPrivateIpAddress-from-phase1-outputs>` is `10.20.1.4` if you used the
default parameters, or whatever you set `dcPrivateIp` to.

## Verify

```
az network vnet show -g rg-wincert-test-lab -n vnet-wincert-lab --query "dhcpOptions.dnsServers"
```

should return the domain controller's IP. Once this matches, proceed to
`docs/04-phase2-deploy.md`.
