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
using System.Text;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win
{
    public class Inventory : IInventoryJobExtension
    {
        public string ExtensionName => "Win";

        private ILogger _logger;
        private IPAMSecretResolver _resolver;

        private string ServerUserName { get; set; }
        private string ServerPassword { get; set; }

        public Inventory(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
            
            _logger = LogHandler.GetClassLogger<Inventory>();
            _logger.MethodEntry();
        }

        public JobResult ProcessJob(InventoryJobConfiguration jobConfiguration, SubmitInventoryUpdate submitInventoryUpdate)
        {
            return PerformInventory(jobConfiguration, submitInventoryUpdate);
        }

        public string ResolvePamField(string name, string value)
        {
            _logger.LogTrace($"Attempting to resolve PAM eligible field {name}");
            return _resolver.Resolve(value);
        }

        private JobResult PerformInventory(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            try
            {
                ServerUserName = ResolvePamField("Server UserName", config.ServerUsername);
                ServerPassword = ResolvePamField("Server Password", config.ServerPassword);

                _logger.LogTrace($"Job Configuration: {JsonConvert.SerializeObject(config)}");

                var storePath = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                var inventoryItems = new List<CurrentInventoryItem>();

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
