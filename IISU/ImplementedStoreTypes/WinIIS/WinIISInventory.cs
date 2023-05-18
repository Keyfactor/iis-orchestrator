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
using Microsoft.Extensions.Logging;
using Microsoft.PowerShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    internal class WinIISInventory : ClientPSCertStoreInventory
    {
        public WinIISInventory(ILogger logger) : base(logger)
        {
        }

        public List<CurrentInventoryItem> GetInventoryItems(Runspace runSpace, string storePath)
        {
            // Get the raw certificate inventory from cert store
            List<Certificate> certificates = base.GetCertificatesFromStore(runSpace, storePath);

            // Contains the inventory items to be sent back to KF
            List<CurrentInventoryItem> myBoundCerts = new List<CurrentInventoryItem>();

            using (PowerShell ps = PowerShell.Create())
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
                    var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                }

                if (iisBindings.Count == 0)
                {
                    return myBoundCerts;
                }

                //in theory should only be one, but keeping for future update to chance inventory
                foreach (var binding in iisBindings)
                {
                    var thumbPrint = $"{(binding.Properties["thumbprint"]?.Value)}";
                    if (string.IsNullOrEmpty(thumbPrint)) continue;

                    Certificate foundCert = certificates.Find(m => m.Thumbprint.Equals(thumbPrint));

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
            }

            return myBoundCerts;
        }
    }
}
