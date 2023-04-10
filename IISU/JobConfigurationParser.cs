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

            // JobProperties
            JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            managementParser.JobConfigurationProperties = jobProperties;

            // PreviousInventoryItem
            managementParser.LastInventory = config.LastInventory;

            //CertificateStore
            managementParser.CertificateStoreDetails.ClientMachine = config.CertificateStoreDetails.ClientMachine;
            managementParser.CertificateStoreDetails.StorePath = config.CertificateStoreDetails.StorePath;
            managementParser.CertificateStoreDetails.StorePassword = "**********";
            managementParser.CertificateStoreDetails.Type = config.CertificateStoreDetails.Type;

            bool isEmpty = (config.JobProperties.Count == 0);       // Check if the dictionary is empty or not
            if (!isEmpty)
            {
                managementParser.CertificateStoreDetailProperties.SiteName = config.JobProperties["SiteName"].ToString();
                managementParser.CertificateStoreDetailProperties.Port = config.JobProperties["Port"].ToString();
                managementParser.CertificateStoreDetailProperties.HostName = config.JobProperties["HostName"]?.ToString();
                managementParser.CertificateStoreDetailProperties.Protocol = config.JobProperties["Protocol"].ToString();
                managementParser.CertificateStoreDetailProperties.SniFlag = config.JobProperties["SniFlag"].ToString()?[..1];
                managementParser.CertificateStoreDetailProperties.IPAddress = config.JobProperties["IPAddress"].ToString();
                managementParser.CertificateStoreDetailProperties.ProviderName = config.JobProperties["ProviderName"]?.ToString();
                managementParser.CertificateStoreDetailProperties.SAN = config.JobProperties["SAN"]?.ToString();
            }

            // Management Base
            managementParser.OperationType = config.OperationType;
            managementParser.Overwrite = config.Overwrite;

            // JobCertificate
            managementParser.JobCertificateProperties.Thumbprint = config.JobCertificate.Thumbprint;
            managementParser.JobCertificateProperties.Contents = config.JobCertificate.Contents;
            managementParser.JobCertificateProperties.Alias = config.JobCertificate.Alias;
            managementParser.JobCertificateProperties.PrivateKeyPassword = "**********";

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
