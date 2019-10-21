using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Interfaces;

using Microsoft.Web.Administration;

namespace IISWithBindings
{
    public class Inventory : IAgentJobExtension
    {
        public string GetJobClass()
        {
            return "Inventory";
        }

        public string GetStoreType()
        {
            return "IISBinding";
        }

        public AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {
            List<AgentCertStoreInventoryItem> inventoryItems = new List<AgentCertStoreInventoryItem>();

            using (X509Store certStore = new X509Store($@"\\{config.Store.ClientMachine}\My", StoreLocation.LocalMachine))
            {
                try
                {
                    certStore.Open(OpenFlags.MaxAllowed);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
                }

                using (ServerManager serverManager = ServerManager.OpenRemote(config.Store.ClientMachine))
                {
                    StorePath storePath = new StorePath();
                    try
                    { 
                        storePath = StorePath.Split(config.Store.StorePath);
                    }
                    catch (InvalidStorePathException ex)
                    {
                        return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
                    }

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
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }
    }
}