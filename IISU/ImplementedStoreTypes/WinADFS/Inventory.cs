// Copyright 2025 Keyfactor
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
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinAdfs;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinAdfs
{
    public class Inventory : WinCertJobTypeBase, IInventoryJobExtension
    {
        private ILogger _logger;
        public string ExtensionName => "WinADFSInventory";

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        Collection<PSObject>? results = null;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.


        public Inventory()
        {
            _logger = LogHandler.GetClassLogger<Inventory>();
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
                    inventoryItems = QueryWinADFSCertificates(settings, storePath);

                    _logger.LogTrace("Invoking submitInventory..");
                    submitInventoryUpdate.Invoke(inventoryItems);
                    _logger.LogTrace($"submitInventory Invoked... {inventoryItems.Count} Items");
                    
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Success,
                        JobHistoryId = jobConfiguration.JobHistoryId,
                        FailureMessage = $"Inventory completed returning {inventoryItems.Count} Items."
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

                var failureMessage = $"Inventory job failed for Site '{jobConfiguration.CertificateStoreDetails.StorePath}' on server '{jobConfiguration.CertificateStoreDetails.ClientMachine}' with error: '{ex.Message}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = jobConfiguration.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }

        public List<CurrentInventoryItem> QueryWinADFSCertificates(RemoteSettings settings, string StoreName)
        {
            _logger.MethodEntry();
            List<CurrentInventoryItem> Inventory = new();

            using (PSHelper ps = new(settings.Protocol, settings.Port, settings.IncludePortInSPN, settings.ClientMachineName, settings.ServerUserName, settings.ServerPassword, true))
            {
                ps.Initialize();

                // Get ADFS Certificates
                results = ps.InvokeFunction("Get-AdfsCertificateInventory");
                if (results == null || results.Count == 0)
                {
                    throw new Exception("No ADFS certificates were found on the target machine.");
                }

                var AdfsCertificates = new List<AdfsCertificateInfo>();

                foreach (PSObject result in results)
                {
                    AdfsCertificates.Add(new AdfsCertificateInfo
                    {
                        CertificateType = GetPropertyValue(result, "CertificateType"),
                        IsPrimary = bool.Parse(GetPropertyValue(result, "IsPrimary") ?? "false"),
                        Thumbprint = GetPropertyValue(result, "Thumbprint"),
                        Subject = GetPropertyValue(result, "Subject"),
                        Issuer = GetPropertyValue(result, "Issuer"),
                        NotBefore = DateTime.Parse(GetPropertyValue(result, "NotBefore")),
                        NotAfter = DateTime.Parse(GetPropertyValue(result, "NotAfter")),
                        DaysUntilExpiry = int.Parse(GetPropertyValue(result, "DaysUntilExpiry") ?? "0"),
                        IsExpired = bool.Parse(GetPropertyValue(result, "IsExpired") ?? "false"),
                        IsExpiringSoon = bool.Parse(GetPropertyValue(result, "IsExpiringSoon") ?? "false")
                    });
                }

                //

                var adfsThumbprint = AdfsCertificates
                    .FirstOrDefault(cert => cert.CertificateType == "Service-Communications" && cert.IsPrimary)?.Thumbprint;

                var parameters = new Dictionary<string, object>
                {
                    { "StoreName", StoreName },
                    { "Thumbprint", adfsThumbprint }
                };

                results = ps.ExecutePowerShell("Get-KFCertificates", parameters);

                // If there are certificates, de-serialize the results and send them back to command
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
                                Certificates = new[] { cert.Base64Data },
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

        /// <summary>
        /// Helper method to get property value from PSObject
        /// </summary>
        private string GetPropertyValue(PSObject psObject, string propertyName)
        {
            try
            {
                return psObject.Properties[propertyName]?.Value?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
