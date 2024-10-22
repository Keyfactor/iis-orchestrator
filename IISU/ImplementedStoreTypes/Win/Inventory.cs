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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.Win;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinIIS;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert
{
    public class Inventory : WinCertJobTypeBase, IInventoryJobExtension
    {
        private ILogger _logger;
        public string ExtensionName => "WinCertInventory";
        
        Collection<PSObject>? results = null;
        
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

                // Deserialize specific job properties
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(jobConfiguration.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool IncludePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = jobConfiguration.CertificateStoreDetails.ClientMachine;
                string storePath = jobConfiguration.CertificateStoreDetails.StorePath;

                if (storePath != null)
                {
                    // Create the remote connection class to pass to Inventory Class
                    RemoteSettings settings = new();
                    settings.ClientMachineName = jobConfiguration.CertificateStoreDetails.ClientMachine;
                    settings.Protocol = jobProperties.WinRmProtocol;
                    settings.Port = jobProperties.WinRmPort;
                    settings.IncludePortInSPN = jobProperties.SpnPortFlag;
                    settings.ServerUserName = serverUserName;
                    settings.ServerPassword = serverPassword;

                    _logger.LogTrace($"Querying Window certificate in store: {storePath}");
                    inventoryItems = QueryWinCertCertificates(settings, storePath);

                    _logger.LogTrace("Invoking submitInventory..");
                    submitInventoryUpdate.Invoke(inventoryItems);
                    _logger.LogTrace($"submitInventory Invoked... {inventoryItems.Count} Items");

                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Success,
                        JobHistoryId = jobConfiguration.JobHistoryId,
                        FailureMessage = ""
                    };
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Warning,
                    JobHistoryId = jobConfiguration.JobHistoryId,
                    FailureMessage =
                        $"No certificates were found in the Certificate Store Path: {storePath} on server: {clientMachineName}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                var failureMessage = $"Inventory job failed for Site '{jobConfiguration.CertificateStoreDetails.StorePath}' on server '{jobConfiguration.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = jobConfiguration.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }

        }

        public List<CurrentInventoryItem> QueryWinCertCertificates(RemoteSettings settings, string StoreName)
        {
            List<CurrentInventoryItem> Inventory = new();

            string command = string.Empty;

            using (PSHelper ps = new(settings.Protocol, settings.Port, settings.IncludePortInSPN, settings.ClientMachineName, settings.ServerUserName, settings.ServerPassword))
            {
                ps.Initialize();

                command = $"Get-KFCertificates -StoreName '{StoreName}'";
                results = ps.ExecuteFunction(command);

                // If there are certificates, deserialize the results and send them back to command
                if (results != null && results.Count > 0)
                {
                    var jsonResults = results[0].ToString();
                    var certInfoList = Certificate.Utilities.DeserializeCertificates<WinCertCertificateInfo>(jsonResults); // JsonConvert.DeserializeObject<List<IISCertificateInfo>>(jsonResults);

                    foreach (WinCertCertificateInfo cert in certInfoList)
                    {
                        var siteSettingsDict = new Dictionary<string, object>
                                {
                                    { "ProviderName", cert.ProviderName},
                                    { "SAN", cert.SAN }
                                };

                        Inventory.Add(
                            new CurrentInventoryItem
                            {
                                Certificates = new[] { cert.Base64Data},
                                Alias = cert.Thumbprint,
                                PrivateKeyEntry = cert.HasPrivateKey,
                                UseChainLevel = false,
                                ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                                Parameters = siteSettingsDict
                            }
                        );
                    }
                }
                ps.Terminate();
            }

            return Inventory;
        }
    }
}
