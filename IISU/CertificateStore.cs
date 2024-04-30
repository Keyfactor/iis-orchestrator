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

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class CertificateStore
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

            // Open with value of 5 means:  Open existing only (4) + Open ReadWrite (1)
            var removeScript = $@"
                        $ErrorActionPreference = 'Stop'
                        $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{StorePath}','LocalMachine')
                        $certStore.Open(5)
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
                        RawData = (byte[])c.Properties["RawData"]?.Value
                    });
            }
            catch (Exception ex)
            {
                throw new CertificateStoreException(
                    $"Error listing certificate in {StorePath} store on {ServerName}: {ex.Message}");
            }
        }

        private static List<Certificate> PerformGetCertificateInvenotory(Runspace runSpace, string storePath)
        {
            List<Certificate> myCertificates = new List<Certificate>();
            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = runSpace;

                var certStoreScript = $@"
                                $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('{storePath}','LocalMachine')
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
                    myCertificates.Add(new Certificate
                    {
                        Thumbprint = $"{c.Properties["Thumbprint"]?.Value}",
                        HasPrivateKey = bool.Parse($"{c.Properties["HasPrivateKey"]?.Value}"),
                        RawData = (byte[])c.Properties["RawData"]?.Value
                    });

                return myCertificates;
            }
            catch (Exception ex)
            {
                throw new CertificateStoreException(
                    $"Error listing certificate in {storePath} store on {runSpace.ConnectionInfo.ComputerName}: {ex.Message}");
            }

        }

        public static List<Certificate> GetCertificatesFromStore(Runspace runSpace, string storePath)
        {
            return PerformGetCertificateInvenotory(runSpace, storePath);
        }

        public static List<CurrentInventoryItem> GetIISBoundCertificates(Runspace runSpace, string storePath)
        {
            List<Certificate> myCertificates = PerformGetCertificateInvenotory(runSpace, storePath);
            List<CurrentInventoryItem> myBoundCerts = new List<CurrentInventoryItem>();

            using (var ps = PowerShell.Create())
            {
                ps.Runspace = runSpace;

                ps.AddCommand("Import-Module")
                    .AddParameter("Name", "WebAdministration")
                    .AddStatement();

                var searchScript = "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                ps.AddScript(searchScript).AddStatement();
                var iisBindings = ps.Invoke();  // Responsible for getting all bound certificates for each website

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll().Aggregate(string.Empty, (current, error) => current + error.ErrorDetails.Message);
                }

                if (iisBindings.Count == 0)
                {
                    return myBoundCerts;
                }

                //in theory should only be one, but keeping for future update to chance inventory
                foreach (var binding in iisBindings)
                {
                    var thumbPrint = $"{binding.Properties["thumbprint"]?.Value}";
                    if (string.IsNullOrEmpty(thumbPrint)) continue;

                    Certificate foundCert = myCertificates.Find(m => m.Thumbprint.Equals(thumbPrint));

                    if (foundCert == null) continue;

                    var sniValue = "";
                    switch (Convert.ToInt16(binding.Properties["sniFlg"]?.Value))
                    {
                        case 0:
                            sniValue = "0 - No SNI";
                            break;
                        case 1:
                            sniValue = "1 - SNI Enabled";
                            break;
                        case 2:
                            sniValue = "2 - Non SNI Binding";
                            break;
                        case 3:
                            sniValue = "3 - SNI Binding";
                            break;
                    }

                    var siteSettingsDict = new Dictionary<string, object>
                             {
                                 { "SiteName", binding.Properties["Name"]?.Value },
                                 { "Port", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[1] },
                                 { "IPAddress", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[0] },
                                 { "HostName", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[2] },
                                 { "SniFlag", sniValue },
                                 { "Protocol", binding.Properties["Protocol"]?.Value }
                             };

                    myBoundCerts.Add(
                        new CurrentInventoryItem
                        {
                            Certificates = new[] { foundCert.CertificateData },
                            Alias = thumbPrint,
                            PrivateKeyEntry = foundCert.HasPrivateKey,
                            UseChainLevel = false,
                            ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                            Parameters = siteSettingsDict
                        }
                    );
                }

                return myBoundCerts;
            }
        }
    }
}