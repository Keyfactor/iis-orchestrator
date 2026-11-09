# 6. Verification runbook

Run these checks at each gate before moving to the next step. All the
`az vm run-command invoke` calls below work without needing to RDP in.

## After Phase 1 (before the DNS cutover)

```
az vm run-command invoke -g rg-wincert-test-lab -n dc-01 \
  --command-id RunPowerShellScript \
  --scripts "Get-Service NTDS | Select Status; Get-ADDomain | Select DNSRoot, DomainMode"
```

Expect `NTDS` status `Running` and a `DNSRoot` matching your `domainName`
parameter (`vmlab.local` by default). If this fails, `dc-01` hasn't finished
promoting yet - wait a few minutes and retry before doing anything else.

Optionally, RDP in (allowed from your `sourceIpAddress`) and confirm:
```
Resolve-DnsName vmlab.local
nslookup dc-01.vmlab.local
```
both resolve to `dc-01`'s own static private IP.

## After Phase 2

**Domain join**, across all five member VMs in one pass. In bash:
```
for vm in adfs-01 adfs-02 iis-01 sql-01 dev-01; do
  echo "=== $vm ==="
  az vm run-command invoke -g rg-wincert-test-lab -n $vm \
    --command-id RunPowerShellScript \
    --scripts "(Get-WmiObject Win32_ComputerSystem).Domain"
done
```
Or in PowerShell:
```powershell
foreach ($vm in 'adfs-01', 'adfs-02', 'iis-01', 'sql-01', 'dev-01') {
    Write-Output "=== $vm ==="
    az vm run-command invoke -g rg-wincert-test-lab -n $vm `
        --command-id RunPowerShellScript `
        --scripts "(Get-WmiObject Win32_ComputerSystem).Domain"
}
```
Each should report `vmlab.local`.

**ADFS farm** (on `adfs-01`):
```
az vm run-command invoke -g rg-wincert-test-lab -n adfs-01 \
  --command-id RunPowerShellScript \
  --scripts "Get-AdfsFarmInformation | Select FarmNodes; Get-AdfsCertificate -CertificateType Service-Communications | Select Thumbprint, Subject"
```
`FarmNodes` should list both `adfs-01` and `adfs-02`.

**SQL Server** (on `sql-01`):
```
az vm run-command invoke -g rg-wincert-test-lab -n sql-01 \
  --command-id RunPowerShellScript \
  --scripts "Invoke-Sqlcmd -ServerInstance localhost -Query 'SELECT @@VERSION'"
```

**IIS** (on `iis-01`):
```
az vm run-command invoke -g rg-wincert-test-lab -n iis-01 \
  --command-id RunPowerShellScript \
  --scripts "Import-Module WebAdministration; Get-Website; Get-WebBinding -Name 'Default Web Site'"
```
Expect a `443` HTTPS binding on the Default Web Site.

**Dev box** (on `dev-01`) - the most important check, since this is where
you'll build and debug the orchestrator from:
```
az vm run-command invoke -g rg-wincert-test-lab -n dev-01 \
  --command-id RunPowerShellScript \
  --scripts "dotnet --version; git --version; pwsh -v; Test-WSMan -ComputerName dc-01; Test-WSMan -ComputerName adfs-01; Test-WSMan -ComputerName adfs-02; Test-WSMan -ComputerName iis-01; Test-WSMan -ComputerName sql-01"
```
Every `Test-WSMan` call should return without an error - this confirms the
WinRM path the extension actually depends on is reachable end-to-end across
the whole lab, not just that the VMs exist.

If any of these come back wrong, re-check the specific bootstrap script's
run-command output before assuming the lab itself is broken:
```
az vm run-command show -g rg-wincert-test-lab --vm-name <vmName> --run-command-name <bootstrapScriptName> --instance-view
```
