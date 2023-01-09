// Copyright 2022 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities
{
    internal class CertificateStore
    {
        public CertificateStore(string serverName, string storePath, Runspace runSpace)
        {
            ServerName = serverName;
            StorePath = storePath;
            RunSpace = runSpace;
            Initalize();
        }

        public string ServerName { get; set; }
        public string StorePath { get; set; }
        public Runspace RunSpace { get; set; }
        public List<Certificate> Certificates { get; set; }

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
                throw new CertificateStoreException($"Error removing certificate in {StorePath} store on {ServerName}.");
        }

        private void Initalize()
        {
            Certificates = new List<Certificate>();
            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = RunSpace;

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
                    Certificates.Add(new Certificate
                    {
                        Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                        HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                        RawData = (byte[]) c.Properties["RawData"]?.Value
                    });
            }
            catch (Exception ex)
            {
                throw new CertificateStoreException(
                    $"Error listing certificate in {StorePath} store on {ServerName}: {ex.Message}");
            }
        }
    }
}