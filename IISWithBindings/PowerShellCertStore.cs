using Keyfactor.Extensions.Orchestrator.IISWithBinding;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    class PowerShellCertStore
    {
        public string ServerName { get; set; }
        public string StorePath { get; set; }
        public Runspace Runspace { get; set; }
        public List<PSCertificate> Certificates { get; set; }
        public PowerShellCertStore(string serverName, string storePath, Runspace runspace)
        {
            ServerName = serverName;
            StorePath = storePath;
            Runspace = runspace;
            Initalize();
        }

        public void RemoveCertificate(string thumbprint)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.Runspace = Runspace;
                    string removeScript = $@"
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

                    var certs = ps.Invoke();
                    if (ps.HadErrors)
                    {
                        throw new PSCertStoreException($"Error removing certificate in {StorePath} store on {ServerName}.");
                    }
                } 
            }
            catch (Exception)
            {

                throw;
            }
        }

        private void Initalize()
        {
            Certificates = new List<PSCertificate>();
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                   
                    ps.Runspace = Runspace;
                    //todo: accept StoreType and Store Name enum for which to open
                    string certStoreScript = $@"
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
                    {
                        Certificates.Add(new PSCertificate() { 
                            Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                            HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                            RawData = (byte[])c.Properties["RawData"]?.Value 
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new PSCertStoreException($"Error listing certificate in {StorePath} store on {ServerName}: {ex.Message}");
            }
        }
    }
}
