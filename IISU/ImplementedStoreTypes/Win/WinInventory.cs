// Copyright 2023 Keyfactor
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
using System;
using System.Collections.Generic;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert
{
    internal class WinInventory : ClientPSCertStoreInventory
    {
        public WinInventory(ILogger logger) : base(logger)
        {
        }

        public List<CurrentInventoryItem> GetInventoryItems(Runspace runSpace, string storePath)
        {
            List<CurrentInventoryItem> inventoryItems = new List<CurrentInventoryItem>();

            foreach (Certificate cert in base.GetCertificatesFromStore(runSpace, storePath))
            {
                var entryParms = new Dictionary<string, object>
                        {
                            { "ProviderName", cert.CryptoServiceProvider },
                            { "SAN", cert.SAN }
                        };


                inventoryItems.Add(new CurrentInventoryItem
                {
                    Certificates = new[] { cert.CertificateData },
                    Alias = cert.Thumbprint,
                    PrivateKeyEntry = cert.HasPrivateKey,
                    UseChainLevel = false,
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    Parameters = entryParms
                });
            }

            return inventoryItems;
        }
    }
}
