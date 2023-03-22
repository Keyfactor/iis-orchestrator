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
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
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
        public string ExtensionName => string.Empty;

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

            return PerformInventory(jobConfiguration, submitInventoryUpdate);
        }

        private JobResult PerformInventory(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            try
            {
                var inventoryItems = new List<CurrentInventoryItem>();

                _logger.LogTrace(JobConfigurationParser.ParseInventoryJobConfiguration(config));

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                // Deserialize specific job properties
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool IncludePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = config.CertificateStoreDetails.ClientMachine;
                string storePath = config.CertificateStoreDetails.StorePath;

                if (storePath != null)
                {
                    _logger.LogTrace($"Establishing runspace on client machine: {clientMachineName}");
                    using var myRunspace = PSHelper.GetClientPSRunspace(protocol, clientMachineName, port, IncludePortInSPN, serverUserName, serverPassword);
                    myRunspace.Open();

                    _logger.LogTrace("Runspace is now open");
                    _logger.LogTrace($"Attempting to read certificates from cert store: {storePath}");

                    //foreach (Certificate cert in PowerShellUtilities.CertificateStore.GetCertificatesFromStore(myRunspace, storePath))
                    WinInventory winInv = new WinInventory(_logger);
                    inventoryItems = winInv.GetInventoryItems(myRunspace, storePath);

                    _logger.LogTrace($"A total of {inventoryItems.Count} were found");
                    _logger.LogTrace("Closing runspace");
                    myRunspace.Close();

                    _logger.LogTrace("Invoking Inventory...");
                    submitInventory.Invoke(inventoryItems);
                    _logger.LogTrace($"Inventory Invoked ... {inventoryItems.Count} Items");

                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Success,
                        JobHistoryId = config.JobHistoryId,
                        FailureMessage = ""
                    };
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Warning,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage =
                        $"No certificates were found in the Certificate Store Path: {storePath} on server: {clientMachineName}"
                };
            }
            catch (CertificateStoreException psEx)
            {
                _logger.LogTrace(psEx.Message);
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage =
                        $"Unable to open remote certificate store: {LogHandler.FlattenException(psEx)}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                var failureMessage = $"Inventory job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }
    }
}
