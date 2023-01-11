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

                // Setup a new connection to the client machine
                var connectionInfo = new WSManConnectionInfo(new Uri($"{storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman"));
                _logger.LogTrace($"WinRm URL: {storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman");

                if (storePath != null)
                {
                    // Set credentials object
                    var pw = new NetworkCredential(ServerUserName, ServerPassword).SecurePassword;

                    connectionInfo.Credential = new PSCredential(ServerUserName, pw);

                    // Create the PowerShell Runspace
                    using var runSpace = RunspaceFactory.CreateRunspace(connectionInfo);
                    _logger.LogTrace("runSpace Created");
                    runSpace.Open();
                    _logger.LogTrace("runSpace Opened");

                    using (var ps = PowerShell.Create())
                    {
                        ps.Runspace = runSpace;
                        _logger.LogTrace("RunSpace Created");

                        try
                        {
                            // Call PowerShell Command to get child items (certs)
                            _logger.LogTrace($"Attempting to get licenses from cert path: {config.CertificateStoreDetails.StorePath})");
                            List<X509Certificate2> myCerts =
                            PSCommandHelper.GetChildItem(ps, config.CertificateStoreDetails.StorePath);

                            if(myCerts.Count > 0)
                            {
                                _logger.LogTrace($"Found {myCerts.Count} certificates in path: {config.CertificateStoreDetails.StorePath}");
                                foreach (X509Certificate2 thisCert in myCerts)
                                {
                                    CurrentInventoryItem inventoryItem = new CurrentInventoryItem()
                                    {
                                        Certificates = new[] { thisCert.GetRawCertDataString().ToString() },
                                        Alias = thisCert.Thumbprint,
                                        PrivateKeyEntry = thisCert.HasPrivateKey,
                                        UseChainLevel = false,
                                        ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                                        Parameters = null
                                    };

                                    inventoryItems.Add(inventoryItem);
                                }

                                // Get Certificate info and add to list of inventory items to pass back to KF
                                _logger.LogTrace("Invoking Inventory...");
                                submitInventory.Invoke(inventoryItems);
                                _logger.LogTrace($"Inventory Invoked ... {inventoryItems.Count} Items");
                            }
                            else
                            {
                                return new JobResult
                                {
                                    Result = OrchestratorJobStatusJobResult.Warning,
                                    JobHistoryId = config.JobHistoryId,
                                    FailureMessage =
                                        $"No certificates were found in the Certificate Store Path: {config.CertificateStoreDetails.StorePath} on server: {config.CertificateStoreDetails.ClientMachine}"
                                };
                            }
                        }
                        catch (CertificateStoreException certEx)
                        {
                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage =
                                    $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}:  failed with Error: {certEx.Message}"
                            };
                        }
                    }
                }

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
