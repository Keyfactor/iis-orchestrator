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
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    abstract class ClientPSCertStoreInventory
    {
        private ILogger _logger;
        public ClientPSCertStoreInventory(ILogger logger)
        {
            _logger = logger;
        }

        public List<Certificate> GetCertificatesFromStore(Runspace runSpace, string storePath)
        {
            List<Certificate> myCertificates = new List<Certificate>();
            try
            {
                using var ps = PowerShell.Create();

                _logger.MethodEntry();

                ps.Runspace = runSpace;

                var certStoreScript = $@"
                                $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{storePath}','LocalMachine')
                                $certStore.Open('ReadOnly')
                                $certs = $certStore.Certificates
                                $certStore.Close()
                                $certStore.Dispose()
                                    $certs | ForEach-Object {{
                                        $certDetails = @{{
                                            Subject = $_.Subject
                                            Thumbprint = $_.Thumbprint
                                            HasPrivateKey = $_.HasPrivateKey
                                            RawData = $_.RawData
                                            san = $_.Extensions | Where-Object {{ $_.Oid.FriendlyName -eq ""Subject Alternative Name"" }} | ForEach-Object {{ $_.Format($false) }}
                                        }}

                                        if ($_.HasPrivateKey) {{
                                            $certDetails.CSP = $_.PrivateKey.CspKeyContainerInfo.ProviderName
                                        }}

                                        New-Object PSObject -Property $certDetails
                                }}";

                ps.AddScript(certStoreScript);

                _logger.LogTrace($"Executing the following script:\n{certStoreScript}");

                var certs = ps.Invoke();

                foreach (var c in certs)
                {
                    myCertificates.Add(new Certificate
                    {
                        Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                        HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                        RawData = (byte[])c.Properties["RawData"]?.Value,
                        CryptoServiceProvider = $"{c.Properties["CSP"]?.Value }",
                        SAN = Certificate.Utilities.FormatSAN($"{c.Properties["san"]?.Value}")  
                    });
                }
                _logger.LogTrace($"found: {myCertificates.Count} certificate(s), exiting GetCertificatesFromStore()");
                return myCertificates;
            }
            catch (Exception ex)
            {
                _logger.LogTrace($"An error occurred in the WinCert GetCertificatesFromStore method:\n{ex.Message}");

                throw new CertificateStoreException(
                    $"Error listing certificate in {storePath} store on {runSpace.ConnectionInfo.ComputerName}: {ex.Message}");
            }
        }
    }
}
