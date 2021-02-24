using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Interfaces;
using Microsoft.Web.Administration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding2.Jobs
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
            dynamic properties = JsonConvert.DeserializeObject(config.Store.Properties.ToString());
            StorePath storePath = new StorePath(properties.siteName.Value, properties.ipAddress.Value, properties.port.Value, properties.hostName.Value);

            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();

            Logger.Trace($"Begin Inventory for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

            using (X509Store certStore = new X509Store($@"\\{config.Store.ClientMachine}\{config.Store.StorePath}", StoreLocation.LocalMachine))
            {
                try
                {
                    certStore.Open(OpenFlags.MaxAllowed);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    Logger.Trace(ex);
                    return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
                }

                using (ServerManager serverManager = ServerManager.OpenRemote(config.Store.ClientMachine))
                {
                    try
                    {
                        Site site = serverManager.Sites[storePath.SiteName];
                        if (site == null)
                        {
                            return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine} not found." };
                        }

                        foreach (Binding binding in site.Bindings.Where(p => p.Protocol.Equals("https", StringComparison.CurrentCultureIgnoreCase) &&
                                                                             p.BindingInformation.Equals(storePath.FormatForIIS(), StringComparison.CurrentCultureIgnoreCase)).ToList())
                        {
                            string thumbPrint = binding.GetAttributeValue("CertificateHash").ToString();

                            if (string.IsNullOrEmpty(thumbPrint))
                                continue;

                            X509Certificate2Collection x509Certs = certStore.Certificates.Find(X509FindType.FindByThumbprint, thumbPrint, true);
                            if (x509Certs.Count == 0)
                                continue;

                            inventoryItems.Add(
                                new AgentCertStoreInventoryItem()
                                {
                                    Certificates = new string[] { Convert.ToBase64String(x509Certs[0].RawData) },
                                    Alias = thumbPrint,
                                    PrivateKeyEntry = x509Certs[0].HasPrivateKey,
                                    UseChainLevel = false,
                                    ItemStatus = AgentInventoryItemStatus.Unknown
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Trace(ex);
                        return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
                    }
                }
            }

            try
            {
                submitInventory.Invoke(inventoryItems);
                return new AnyJobCompleteInfo() { Status = 2, Message = "Successful" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }
    }
}
