
using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    [Job(JobTypes.INVENTORY)]
    public class Inventory: AgentJob, IAgentJobExtension
    {
        public override AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            return PerformInventory(config,submitInventory);
        }

        private AnyJobCompleteInfo PerformInventory(AnyJobConfigInfo config,SubmitInventoryUpdate submitInventory)
        {
            try
            {
                StorePath storePath = JsonConvert.DeserializeObject<StorePath>(config.Store.Properties.ToString(), new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();

                Logger.Trace($"Begin Inventory for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));
                connInfo.IncludePortInSPN = storePath.SPNPortFlag;
                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;

                        ps.AddCommand("Import-Module")
                            .AddParameter("Name", "WebAdministration")
                            .AddStatement();
                        ps.AddCommand("Get-WebBinding")
                            .AddParameter("Name", storePath.SiteName)
                            .AddParameter("Protocol", storePath.Protocol)
                            .AddParameter("Port", storePath.Port)
                            .AddParameter("HostHeader", storePath.HostName)
                            .AddStatement();

                        var iisBindings = ps.Invoke();

                        if (ps.HadErrors) { 
                            return new AnyJobCompleteInfo() { Status = 4, Message = $"Inventory for Site {storePath.SiteName} on server {config.Store.ClientMachine} failed." };
                        }

                        if (iisBindings.Count == 0){
                            submitInventory.Invoke(inventoryItems);
                            return new AnyJobCompleteInfo() { Status = 3, Message = $"{storePath.Protocol} binding for Site {storePath.SiteName} on server {config.Store.ClientMachine} not found." };
                        }

                        //in theory should only be one, but keeping for future update to chance inventory
                        foreach (var binding in iisBindings)
                        {
                            var thumbPrint = $"{(binding.Properties["certificateHash"]?.Value)}";
                            if (string.IsNullOrEmpty(thumbPrint))
                                continue;

                            var foundCert = psCertStore.Certificates.Find(m => m.Thumbprint.Equals(thumbPrint));

                            if (foundCert == null)
                                continue;

                            inventoryItems.Add(
                                new AgentCertStoreInventoryItem()
                                {
                                    Certificates = new string[] { foundCert.CertificateData },
                                    Alias = thumbPrint,
                                    PrivateKeyEntry = foundCert.HasPrivateKey,
                                    UseChainLevel = false,
                                    ItemStatus = AgentInventoryItemStatus.Unknown
                                }
                            );
                        }
                    }
                    runspace.Close();
                }

                submitInventory.Invoke(inventoryItems);
                return new AnyJobCompleteInfo() { Status = 2, Message = "Successful" };
            }
            catch (PSCertStoreException psEx)
            {
                Logger.Trace(psEx);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Unable to open remote certificate store: {psEx.Message}" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };

            }
        }
    }
}
