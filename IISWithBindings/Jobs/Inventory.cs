using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding.Jobs
{
    public class Inventory : IInventoryJobExtension
    {
        private readonly ILogger<Inventory> _logger;

        public Inventory(ILogger<Inventory> logger) =>
            _logger = logger;

        private JobResult PerformInventory(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            try
            {
                StorePath storePath = JsonConvert.DeserializeObject<StorePath>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                var inventoryItems = new List<CurrentInventoryItem>();

                _logger.LogTrace($"Begin Inventory for Cert Store {$@"\\{config.CertificateStoreDetails.ClientMachine}\{config.CertificateStoreDetails.StorePath}"}");

                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.CertificateStoreDetails.ClientMachine}:5985/wsman"));
                if (storePath != null)
                {
                    SecureString pw = new NetworkCredential(config.ServerUsername, config.ServerPassword)
                        .SecurePassword;
                    connInfo.Credential = new PSCredential(config.ServerUsername, pw);

                    using Runspace runSpace = RunspaceFactory.CreateRunspace(connInfo);
                    runSpace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(
                        config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath,
                        runSpace);
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runSpace;
                        ps.AddCommand("Import-Module")
                            .AddParameter("Name", "WebAdministration")
                            .AddStatement();

                        var searchScript = "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                        ps.AddScript(searchScript).AddStatement();
                        var iisBindings = ps.Invoke();

                        if (ps.HadErrors)
                        {
                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage =
                                    $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}:  failed."
                            };
                        }

                        if (iisBindings.Count == 0)
                        {
                            submitInventory.Invoke(inventoryItems);
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
                            var thumbPrint = $"{(binding.Properties["thumbprint"]?.Value)}";
                            if (string.IsNullOrEmpty(thumbPrint))
                                continue;

                            var foundCert = psCertStore.Certificates.Find(m => m.Thumbprint.Equals(thumbPrint));

                            if (foundCert == null)
                                continue;

                            inventoryItems.Add(
                                new CurrentInventoryItem
                                {
                                    Certificates = new[] {foundCert.CertificateData},
                                    Alias = thumbPrint,
                                    PrivateKeyEntry = foundCert.HasPrivateKey,
                                    UseChainLevel = false,
                                    ItemStatus = OrchestratorInventoryItemStatus.Unknown
                                }
                            );
                        }
                    }

                    runSpace.Close();
                }

                submitInventory.Invoke(inventoryItems);
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
                        $"Unable to open remote certificate store: {psEx.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                string failureMessage = $"Inventory job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{ex.Message}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }

        public string ExtensionName => "IISBindings";
        public JobResult ProcessJob(InventoryJobConfiguration jobConfiguration, SubmitInventoryUpdate submitInventoryUpdate)
        {
            return PerformInventory(jobConfiguration, submitInventoryUpdate);
        }
    }
}
