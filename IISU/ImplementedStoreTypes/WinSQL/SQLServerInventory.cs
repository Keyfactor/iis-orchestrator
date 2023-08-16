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
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

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
            //SqlInstanceName = jobProperties.SqlInstanceName;

            // Get the raw certificate inventory from cert store
            List<Certificate> certificates = base.GetCertificatesFromStore(runSpace, jobConfig.CertificateStoreDetails.StorePath);

            // Contains the inventory items to be sent back to KF
            List<CurrentInventoryItem> myBoundCerts = new List<CurrentInventoryItem>();
            using (PowerShell ps2 = PowerShell.Create())
            {
                    //runSpace.Open();
                    ps2.Runspace = runSpace;

                    //Get all the installed instances of Sql Server
                    var funcScript = string.Format(@$"(Get-ItemProperty ""HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server"").InstalledInstances");
                    ps2.AddScript(funcScript);
                    //.LogTrace("funcScript added...");
                    var instances = ps2.Invoke();
                    ps2.Commands.Clear();
                    var psSqlManager = new ClientPsSqlManager(jobConfig, runSpace);
                    foreach (var instance in instances)
                    {
                        var regLocation = psSqlManager.GetSqlCertRegistryLocation(instance.ToString(), ps2);

                        funcScript = string.Format(@$"Get-ItemPropertyValue ""{regLocation}"" -Name Certificate");
                        ps2.AddScript(funcScript);
                        //_logger.LogTrace("funcScript added...");
                        var thumbprint = ps2.Invoke()[0].ToString();
                        ps2.Commands.Clear();
                        if (string.IsNullOrEmpty(thumbprint)) continue;
                        thumbprint = thumbprint.ToUpper();

                        Certificate foundCert = certificates.Find(m => m.Thumbprint.ToUpper().Equals(thumbprint));

                        if (foundCert == null) continue;

                        var sqlSettingsDict = new Dictionary<string, object>
                             {
                                 { "InstanceName", instance.ToString() }
                             };

                        myBoundCerts.Add(
                        new CurrentInventoryItem
                        {
                            Certificates = new[] { foundCert.CertificateData },
                            Alias = thumbprint,
                            PrivateKeyEntry = foundCert.HasPrivateKey,
                            UseChainLevel = false,
                            ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                            Parameters = sqlSettingsDict
                        });

                    }
                    return myBoundCerts;
            }

        }
    }
}
