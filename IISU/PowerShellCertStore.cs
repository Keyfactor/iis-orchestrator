using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    internal class PowerShellCertStore
    {
        public PowerShellCertStore(string serverName, string storePath, Runspace runSpace)
        {
            ServerName = serverName;
            StorePath = storePath;
            RunSpace = runSpace;
            Initalize();
        }

        public string ServerName { get; set; }
        public string StorePath { get; set; }
        public Runspace RunSpace { get; set; }
        public List<PsCertificate> Certificates { get; set; }

        public void RemoveCertificate(string thumbprint)
        {
            using var ps = PowerShell.Create();
            ps.Runspace = RunSpace;
            var removeScript = $@"
                        $ErrorActionPreference = 'Stop'
                        $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{StorePath}','LocalMachine')
                        $certStore.Open('MaxAllowed')
                        $certToRemove = $certStore.Certificates.Find(0,'{thumbprint}',$false)
                        if($certToRemove.Count -gt 0) {{
                            $certStore.Remove($certToRemove[0])
                        }}
                        $certStore.Close()
                        $certStore.Dispose()
                    ";

            ps.AddScript(removeScript);

            var _ = ps.Invoke();
            if (ps.HadErrors)
                throw new PsCertStoreException($"Error removing certificate in {StorePath} store on {ServerName}.");
        }

        private void Initalize()
        {
            Certificates = new List<PsCertificate>();
            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = RunSpace;
                //todo: accept StoreType and Store Name enum for which to open
                var certStoreScript = $@"
                                $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{StorePath}','LocalMachine')
                                $certStore.Open('ReadOnly')
                                $certs = $certStore.Certificates
                                $certStore.Close()
                                $certStore.Dispose()
                                foreach ( $cert in $certs){{ 
                                    $cert | Select-Object -Property Thumbprint, RawData, HasPrivateKey
                                }}";

                ps.AddScript(certStoreScript);

                var certs = ps.Invoke();

                foreach (var c in certs)
                    Certificates.Add(new PsCertificate
                    {
                        Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                        HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                        RawData = (byte[]) c.Properties["RawData"]?.Value
                    });
            }
            catch (Exception ex)
            {
                throw new PsCertStoreException(
                    $"Error listing certificate in {StorePath} store on {ServerName}: {ex.Message}");
            }
        }
    }
}