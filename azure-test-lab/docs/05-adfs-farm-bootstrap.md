# 5. ADFS farm bootstrap: what's actually happening, and the question this lab exists to answer

## The mechanics

1. **Service account, not gMSA.** `svc-adfs` is a plain domain user, added
   to the local Administrators group on both `adfs-01` and `adfs-02`. A
   group Managed Service Account (gMSA) is the more "production-like"
   choice, but retrieving one requires a KDS root key that's been active on
   the domain for up to 10 hours - a real wait in a lab you might want to
   stand up and tear down the same day. If you want to test the gMSA path
   instead, after Phase 1's forest promotion, run on `dc-01`:
   ```
   Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))
   ```
   before Phase 2 deploys, then adapt `Bootstrap-AdfsPrimary.ps1` /
   `Bootstrap-AdfsSecondary.ps1` to use `-GroupServiceAccountIdentifier`
   instead of `-ServiceAccountCredential`.

2. **The bootstrap certificate.** `Install-AdfsFarm` requires a certificate
   already sitting in `adfs-01`'s `LocalMachine\My` store, with a SAN
   matching the federation service name (`sts.vmlab.local` by default).
   `Bootstrap-AdfsPrimary.ps1` generates one with `New-SelfSignedCertificate`.
   This is deliberately the same "Service Communications" certificate the
   `WinAdfs` store type in this repo is built to rotate - so once the
   Universal Orchestrator is installed and pointed at this farm, rotating
   this exact certificate is the natural first WinAdfs test to run.

3. **Getting the same certificate onto both nodes.** `Add-AdfsFarmNode`
   needs `adfs-02` to present the *same* certificate (private key included)
   as the primary. There's no built-in file-copy mechanism between two
   fresh VMs in a Bicep deployment, so `Bootstrap-AdfsPrimary.ps1` exports
   the certificate as a password-protected PFX to a small SMB share on
   `dc-01` (`\\dc-01\AdfsBootstrap$`, created and locked down to Domain
   Admins by `Initialize-AdfsPrereqs.ps1`), and `Bootstrap-AdfsSecondary.ps1`
   imports it from there. The export password is a fresh GUID
   (`adfsBootstrapCertPassword`, defaults to `newGuid()` in
   `phase2-member-servers/main.bicep`) generated per-deployment and never
   written to disk in plaintext - it only ever exists as a deployment-time
   secure parameter.

4. **DNS for the federation service name.** `sts.vmlab.local` resolves
   directly to `adfs-01`'s static private IP - there's no load balancer in
   this lab. That's fine for exercising the farm's certificate
   rotation/fan-out behavior, but **don't mistake this for a
   highly-available configuration**; a real production ADFS farm sits
   behind a load balancer VIP.

## The question this lab is specifically meant to answer

`docsource/winadfs.md` in this repo states that the Universal Orchestrator
**must** run as a local agent installed on the Primary ADFS server. But the
C# in `IISU/ImplementedStoreTypes/WinADFS/AdfsCertificateRotationManager.cs`
branches on `PSHelper.IsLocalMachine`, and when connected to the primary
node *remotely* over WinRM rather than running locally on it, the code
appears to still attempt the rotation - treating even the "primary" as just
another remote node and opening a fresh WinRM connection to install the
certificate, update farm settings, and restart the service.

This lab doesn't need any extra infrastructure to settle that question:
`dev-01` already has WinRM line-of-sight to both `adfs-01` and `adfs-02`
once you install the real Universal Orchestrator there. Try both:

- **Documented path:** install the orchestrator directly on `adfs-01` as a
  local agent, and configure the WinAdfs store with
  `adfs-01|LocalMachine` as the Client Machine.
- **Code-suggested path:** keep the orchestrator on `dev-01`, and configure
  the WinAdfs store with `adfs-01`'s hostname/IP as a remote WinRM target.

Whichever one actually works (or if both do, or neither does cleanly) is
useful, concrete information about this repo's own behavior that's worth
feeding back into `docsource/winadfs.md` and the WinAdfs implementation.

## If Install-AdfsFarm or Add-AdfsFarmNode fails

AD FS's exact cmdlet requirements have shifted across Windows Server
versions, and these scripts were written against current (Windows Server
2022) documentation - if a farm-creation or farm-join call fails with a
parameter or permissions error that doesn't match what's described above,
check the current Microsoft AD FS deployment documentation for the
Windows Server version actually deployed before assuming the script logic
itself is wrong.
