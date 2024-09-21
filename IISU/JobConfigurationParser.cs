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

using Keyfactor.Orchestrators.Extensions;
using Microsoft.PowerShell.Commands;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration.Internal;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Management.Automation.Remoting;
using System.Net;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class JobConfigurationParser
    {
        public static string ParseManagementJobConfiguration(ManagementJobConfiguration config)
        {

            IManagementJobLogger managementParser = new ManagementJobLogger();

            try
            {
                // JobConfiguration
                managementParser.JobCancelled = config.JobCancelled;
                managementParser.ServerError = config.ServerError;
                managementParser.JobHistoryID = config.JobHistoryId;
                managementParser.RequestStatus = config.RequestStatus;
                managementParser.ServerUserName = config.ServerUsername;
                managementParser.ServerPassword = "**********";
                managementParser.UseSSL = config.UseSSL;
                managementParser.JobTypeID = config.JobTypeId;
                managementParser.JobID = config.JobId;
                managementParser.Capability = config.Capability;

            }
            catch (Exception e)
            {
                throw new Exception($"Error while paring management Job Configuration: {e.Message}");
            }

            
            try
            {
                // JobProperties
                JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                managementParser.JobConfigurationProperties = jobProperties;
            }
            catch (Exception e)
            {
                throw new Exception($"Error while parsing management Job Properties: {e.Message}");
            }

            try
            {
                // PreviousInventoryItem
                managementParser.LastInventory = config.LastInventory;
            }
            catch (Exception e)
            {
                throw new Exception($"Error while parsing Previous Inventory Item: {e.Message}");
            }

            try
            {
                //CertificateStore
                managementParser.CertificateStoreDetails.ClientMachine = config.CertificateStoreDetails.ClientMachine;
                managementParser.CertificateStoreDetails.StorePath = config.CertificateStoreDetails.StorePath;
                managementParser.CertificateStoreDetails.StorePassword = "**********";
                managementParser.CertificateStoreDetails.Type = config.CertificateStoreDetails.Type;

                bool isEmpty = (config.JobProperties.Count == 0);       // Check if the dictionary is empty or not
                if (!isEmpty)
                {
                    object value = "";
                    if (config.JobProperties.TryGetValue("SiteName", out value)) managementParser.CertificateStoreDetailProperties.SiteName = config.JobProperties["SiteName"].ToString();
                    if (config.JobProperties.TryGetValue("Port", out value)) managementParser.CertificateStoreDetailProperties.Port = config.JobProperties["Port"].ToString();
                    if (config.JobProperties.TryGetValue("HostName", out value)) managementParser.CertificateStoreDetailProperties.HostName = config.JobProperties["HostName"]?.ToString();
                    if (config.JobProperties.TryGetValue("Protocol", out value)) managementParser.CertificateStoreDetailProperties.Protocol = config.JobProperties["Protocol"].ToString();
                    if (config.JobProperties.TryGetValue("SniFlag", out value)) managementParser.CertificateStoreDetailProperties.SniFlag = config.JobProperties["SniFlag"].ToString();
                    if (config.JobProperties.TryGetValue("IPAddress", out value)) managementParser.CertificateStoreDetailProperties.IPAddress = config.JobProperties["IPAddress"].ToString();
                    if (config.JobProperties.TryGetValue("ProviderName", out value)) managementParser.CertificateStoreDetailProperties.ProviderName = config.JobProperties["ProviderName"]?.ToString();
                    if (config.JobProperties.TryGetValue("SAN", out value)) managementParser.CertificateStoreDetailProperties.SAN = config.JobProperties["SAN"]?.ToString();
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Error while parsing Certificate Store: {e.Message}");
            }

            try
            {
                // Management Base
                managementParser.OperationType = config.OperationType;
                managementParser.Overwrite = config.Overwrite;
            }
            catch (Exception e)
            {
                throw new Exception($"Error while parsing Management Base: {e.Message}");
            }

            try
            {
                // JobCertificate
                managementParser.JobCertificateProperties.Thumbprint = config.JobCertificate.Thumbprint;
                managementParser.JobCertificateProperties.Contents = config.JobCertificate.Contents;
                managementParser.JobCertificateProperties.Alias = config.JobCertificate.Alias;
                managementParser.JobCertificateProperties.PrivateKeyPassword = "**********";
            }
            catch (Exception e)
            {
                throw new Exception($"Error while parsing Job Certificate: {e.Message}");
            }

            return JsonConvert.SerializeObject(managementParser);
        }

        public static string ParseInventoryJobConfiguration(InventoryJobConfiguration config)
        {
            IInventoryJobLogger inventoryParser = new InventoryJobLogger();

            // JobConfiguration
            inventoryParser.JobCancelled = config.JobCancelled;
            inventoryParser.ServerError = config.ServerError;
            inventoryParser.JobHistoryID = config.JobHistoryId;
            inventoryParser.RequestStatus = config.RequestStatus;
            inventoryParser.ServerUserName = config.ServerUsername;
            inventoryParser.ServerPassword = "**********";
            inventoryParser.UseSSL = config.UseSSL;
            inventoryParser.JobTypeID = config.JobTypeId;
            inventoryParser.JobID = config.JobId;
            inventoryParser.Capability = config.Capability;

            // JobProperties
            JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            inventoryParser.JobConfigurationProperties = jobProperties;

            // PreviousInventoryItem
            inventoryParser.LastInventory = config.LastInventory;

            //CertificateStore
            
            inventoryParser.CertificateStoreDetails.ClientMachine = config.CertificateStoreDetails.ClientMachine;
            inventoryParser.CertificateStoreDetails.StorePath = config.CertificateStoreDetails.StorePath;
            inventoryParser.CertificateStoreDetails.StorePassword = "**********";
            inventoryParser.CertificateStoreDetails.Type = config.CertificateStoreDetails.Type;


            return JsonConvert.SerializeObject(inventoryParser);
        }
    }
}
