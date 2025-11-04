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
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinADFS
{
    public class Inventory : WinCertJobTypeBase, IInventoryJobExtension
    {
        private ILogger _logger;
        public string ExtensionName => "WinADFSInventory";

        public Inventory()
        {
                
        }

        public Inventory(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        public JobResult ProcessJob(InventoryJobConfiguration jobConfiguration, SubmitInventoryUpdate submitInventoryUpdate)
        {
            _logger = LogHandler.GetClassLogger<Inventory>();
            _logger.MethodEntry();

            try
            {
                var inventoryItems = new List<CurrentInventoryItem>();

                _logger.LogTrace(JobConfigurationParser.ParseInventoryJobConfiguration(jobConfiguration));

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", jobConfiguration.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", jobConfiguration.ServerPassword);

                // De-serialize specific job properties
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(jobConfiguration.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool IncludePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = jobConfiguration.CertificateStoreDetails.ClientMachine;
                string storePath = jobConfiguration.CertificateStoreDetails.StorePath;

                //using (PSHelper psHelper = new PSHelper(_logger, clientMachineName, serverUserName, serverPassword, protocol, port, IncludePortInSPN))
                //{
                //    results = WinADFSInventory.GetCertificates(psHelper, storePath, _logger);
                //    foreach (var result in results)
                //    {
                //        WinADFSCertificateInfo certInfo = new WinADFSCertificateInfo
                //        {
                //            StoreName = result.Members["StoreName"].Value.ToString(),
                //            Certificate = result.Members["Certificate"].Value.ToString(),
                //            ExpiryDate = result.Members["ExpiryDate"].Value.ToString(),
                //            Issuer = result.Members["Issuer"].Value.ToString(),
                //            Thumbprint = result.Members["Thumbprint"].Value.ToString(),
                //            HasPrivateKey = Convert.ToBoolean(result.Members["HasPrivateKey"].Value),
                //            SAN = result.Members["SAN"].Value.ToString(),
                //            ProviderName = result.Members["ProviderName"].Value.ToString(),
                //            Base64Data = result.Members["Base64Data"].Value.ToString()
                //        };
                //        inventoryItems.Add(ConvertToInventoryItem(certInfo));
                //    }
                //}

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Warning,
                    JobHistoryId = jobConfiguration.JobHistoryId,
                    FailureMessage =
                        $"No certificates were found in the Certificate Store Path: {storePath} on server: {clientMachineName}"
                };
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
