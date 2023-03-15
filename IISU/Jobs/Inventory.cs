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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISU.Jobs
{
    public class Inventory : IInventoryJobExtension
    {
        private ILogger _logger;

        private IPAMSecretResolver _resolver;

        private string ServerUserName { get; set; }
        private string ServerPassword { get; set; }

        public Inventory(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        private string ResolvePamField(string name, string value)
        {
            _logger.LogTrace($"Attempting to resolved PAM eligible field {name}");
            return _resolver.Resolve(value);
        }

        private JobResult PerformInventory(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            try
            {
                _logger = LogHandler.GetClassLogger<Inventory>();
                _logger.MethodEntry();
                ServerUserName = ResolvePamField("Server UserName", config.ServerUsername);
                ServerPassword = ResolvePamField("Server Password", config.ServerPassword);

                _logger.LogTrace($"Job Configuration: {JsonConvert.SerializeObject(config)}");
                var storePath = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                var inventoryItems = new List<CurrentInventoryItem>();

                _logger.LogTrace($"Begin Inventory for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                var connInfo = new WSManConnectionInfo(new Uri($"{storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman"));
                _logger.LogTrace($"WinRm Url: {storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman");

                if (storePath != null)
                {
                    var pw = new NetworkCredential(ServerUserName, ServerPassword)
                        .SecurePassword;
                    _logger.LogTrace($"Credentials: UserName:{ServerUserName} Password:{ServerPassword}");
                    connInfo.Credential = new PSCredential(ServerUserName, pw);
                    _logger.LogTrace($"PSCredential Created {pw}");

                    using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    _logger.LogTrace("runSpace Created");
                    runSpace.Open();
                    _logger.LogTrace("runSpace Opened");

                    var psCertStore = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    _logger.LogTrace("psCertStore Created");

                    using (var ps = PowerShell.Create())
                    {
                        ps.Runspace = runSpace;
                        _logger.LogTrace("RunSpace Created");
                        ps.AddCommand("Import-Module")
                            .AddParameter("Name", "WebAdministration")
                            .AddStatement();
                        _logger.LogTrace("WebAdministration Imported");

                        var searchScript = "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                        _logger.LogTrace($"searchScript {searchScript}");
                        ps.AddScript(searchScript).AddStatement();
                        _logger.LogTrace("searchScript added...");
                        var iisBindings = ps.Invoke();
                        _logger.LogTrace("iisBindings Created...");
                        
                        if (ps.HadErrors)
                        {
                            _logger.LogTrace("ps Has Errors");
                            var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);

                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage =
                                    $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}:  failed with Error: {psError}"
                            };
                        }

                        if (iisBindings.Count == 0)
                        {
                            _logger.LogTrace("submitInventory About To Be Invoked No Bindings Found");
                            submitInventory.Invoke(inventoryItems);
                            _logger.LogTrace("submitInventory Invoked...");
                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Warning,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage =
                                    $"Inventory on server {config.CertificateStoreDetails.ClientMachine} did not find any bindings."
                            };
                        }

                        //in theory should only be one, but keeping for future update to chance inventory
                        foreach (var binding in iisBindings)
                        {
                            _logger.LogTrace("Looping Bindings...");
                            var thumbPrint = $"{(binding.Properties["thumbprint"]?.Value)}";
                            _logger.LogTrace($"thumbPrint: {thumbPrint}");
                            if (string.IsNullOrEmpty(thumbPrint))
                                continue;

                            var foundCert = psCertStore.Certificates.Find(m => m.Thumbprint.Equals(thumbPrint));
                            _logger.LogTrace($"foundCert: {foundCert?.CertificateData}");
                            
                            if (foundCert == null)
                                continue;

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

                            _logger.LogTrace($"bindingSiteName: {binding.Properties["Name"]?.Value}, bindingIpAddress: {binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[0]}, bindingPort: {binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[1]}, bindingHostName: {binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[2]}, bindingProtocol: {binding.Properties["Protocol"]?.Value}, bindingSniFlg: {sniValue}");

                            var siteSettingsDict = new Dictionary<string, object>
                             {
                                 { "SiteName", binding.Properties["Name"]?.Value },
                                 { "Port", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[1] },
                                 { "IPAddress", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[0] },
                                 { "HostName", binding.Properties["Bindings"]?.Value.ToString()?.Split(':')[2] },
                                 { "SniFlag", sniValue },
                                 { "Protocol", binding.Properties["Protocol"]?.Value }
                             };

                            inventoryItems.Add(
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
                    _logger.LogTrace("closing runSpace...");
                    runSpace.Close();
                    _logger.LogTrace("runSpace closed...");
                }
                _logger.LogTrace("Invoking Inventory..");
                submitInventory.Invoke(inventoryItems);
                _logger.LogTrace($"Inventory Invoked... {inventoryItems.Count} Items");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (PsCertStoreException psEx)
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

        public string ExtensionName => "IISU";
        public JobResult ProcessJob(InventoryJobConfiguration jobConfiguration, SubmitInventoryUpdate submitInventoryUpdate)
        {
            return PerformInventory(jobConfiguration, submitInventoryUpdate);
        }
    }
}