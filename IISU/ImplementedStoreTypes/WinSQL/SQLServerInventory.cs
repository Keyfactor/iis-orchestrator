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
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql
{
    internal class SQLServerInventory : ClientPSCertStoreInventory
    {
        private string SqlInstanceName { get; set; }

        public SQLServerInventory(ILogger logger) : base(logger)
        {
        }

        public List<CurrentInventoryItem> GetInventoryItems(Runspace runSpace, InventoryJobConfiguration jobConfig)
        {
            var jobProperties = JsonConvert.DeserializeObject<JobProperties>(jobConfig.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            SqlInstanceName = jobProperties.SqlInstanceName;

            // Get the raw certificate inventory from cert store
            List<Certificate> certificates = base.GetCertificatesFromStore(runSpace, jobConfig.CertificateStoreDetails.StorePath);

            // Contains the inventory items to be sent back to KF
            List<CurrentInventoryItem> myBoundCerts = new List<CurrentInventoryItem>();

            using (PowerShell ps2 = PowerShell.Create())
            {
                ps2.Runspace = runSpace;

                var searchScript = $"Get-ItemProperty -Path \"HKLM:\\SOFTWARE\\Microsoft\\Microsoft SQL Server\\{SqlInstanceName}\\MSSQLServer\\SuperSocketNetLib\" -Name Certificate";
                ps2.AddScript(searchScript);
                var sqlBindings = ps2.Invoke();  // Responsible for getting all bound certificates for each website

                if (ps2.HadErrors)
                {
                    var psError = ps2.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                }

                if (sqlBindings.Count == 0)
                {
                    return myBoundCerts;
                }

                foreach (var binding in sqlBindings)
                {
                    var thumbPrint = $"{(binding.Properties["Certificate"]?.Value)}";
                    if (string.IsNullOrEmpty(thumbPrint)) continue;
                    thumbPrint = thumbPrint.ToUpper();

                    Certificate foundCert = certificates.Find(m => m.Thumbprint.ToUpper().Equals(thumbPrint));

                    if (foundCert == null) continue;

                    //in theory should only be one, but keeping for future update to chance inventory
                    myBoundCerts.Add(
                    new CurrentInventoryItem
                    {
                        Certificates = new[] { foundCert.CertificateData },
                        Alias = thumbPrint,
                        PrivateKeyEntry = foundCert.HasPrivateKey,
                        UseChainLevel = false,
                        ItemStatus = OrchestratorInventoryItemStatus.Unknown
                    }
                );
                }
            }

            return myBoundCerts;
        }
    }
}
