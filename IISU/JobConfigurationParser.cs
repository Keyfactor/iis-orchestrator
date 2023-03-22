using Keyfactor.Orchestrators.Extensions;
using Microsoft.PowerShell.Commands;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class JobConfigurationParser
    {
        public static string ParseManagementJobConfiguration(ManagementJobConfiguration config, bool IncludePreviousInventory = true)
        {
            StringBuilder output = new StringBuilder();

            if (IncludePreviousInventory && config.LastInventory.Count() > 0)
            {
                output.AppendLine("Previous Inventory Items:");
                foreach (PreviousInventoryItem item in config.LastInventory)
                {
                    output.AppendLine($"Alias: {item.Alias}");
                    output.AppendLine($"Alias: {item.PrivateKeyEntry}");
                    foreach (string thumbprint in item.Thumbprints)
                    {
                        output.AppendLine($"Thumbprint: {thumbprint}");
                    }

                    output.AppendLine();        // Blank line
                }
            }

            // Certificate Store Properties
            output.AppendLine("Certificate Store Properties:");
            output.AppendLine($"Type: {config.CertificateStoreDetails.Type}");
            output.AppendLine($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");
            output.AppendLine($"Store Path: {config.CertificateStoreDetails.StorePath}");
            output.AppendLine($"Store Password: **************");

            output.AppendLine();        // Blank line

            output.AppendLine($"Operation Type: {config.OperationType}");
            output.AppendLine($"Overwrite: {config.Overwrite}");

            output.AppendLine();        // Blank line

            output.AppendLine("Certificate Store Properties:");
            output.AppendLine($"Thumbprint: {config.JobCertificate.Thumbprint}");
            output.AppendLine($"Contents: {config.JobCertificate.Contents}");
            output.AppendLine($"Alias: {config.JobCertificate.Alias}");
            output.AppendLine($"Private Key Password: **************");

            output.AppendLine();        // Blank line

            JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            output.AppendLine("Cert Store Job Properties (Contains IIS and non-IIS Properties)");
            output.AppendLine($"SPN With Port: {jobProperties.SpnPortFlag}");
            output.AppendLine($"WinRm Protocol: {jobProperties.WinRmProtocol}");
            output.AppendLine($"WinRm Port: {jobProperties.WinRmPort}");
            output.AppendLine($"Server Username: {jobProperties.ServerUsername}");
            output.AppendLine($"Server Username: ***************");
            output.AppendLine($"Server Use SSL: {jobProperties.ServerUseSsl}");

            output.AppendLine();        // Blank line

            output.AppendLine("Job Configuration Properties:");
            output.AppendLine($"Job Cancelled: {config.JobCancelled}");
            output.AppendLine($"ServerError: {config.ServerError}");
            output.AppendLine($"Job History ID: {config.JobHistoryId}");
            output.AppendLine($"Request Status: {config.RequestStatus}");
            output.AppendLine($"Server Username: {config.ServerUsername}");
            output.AppendLine($"Server Username: ***************");
            output.AppendLine($"Use SSL: {config.UseSSL}");
            output.AppendLine($"Job Type ID: {config.JobTypeId}");
            output.AppendLine($"Job ID: {config.JobId}");
            output.AppendLine($"Capability: {config.Capability}");

            bool isEmpty = (config.JobProperties.Count == 0);       // Check if the dictionary is empty or not
            if (!isEmpty)
            {
                output.AppendLine();        // Blank line
                output.AppendLine($"JSON Job Properties:");
                output.AppendLine($"Site Name: {config.JobProperties["SiteName"].ToString()}");
                output.AppendLine($"Port: {config.JobProperties["Port"].ToString()}");
                output.AppendLine($"Host Name: {config.JobProperties["HostName"]?.ToString()}");
                output.AppendLine($"Protocol: {config.JobProperties["Protocol"].ToString()}");
                output.AppendLine($"SniFlag: {config.JobProperties["SniFlag"].ToString()?[..1]}");
                output.AppendLine($"IP Address: {config.JobProperties["IPAddress"].ToString()}");
                output.AppendLine($"SAN: {config.JobProperties["SAN"]?.ToString()}");
            }

            return output.ToString();
        }

        public static string ParseInventoryJobConfiguration(InventoryJobConfiguration config, bool IncludePreviousInventory = true)
        {
            StringBuilder output = new StringBuilder();

            if (IncludePreviousInventory && config.LastInventory.Count() > 0)
            {
                output.AppendLine("Previous Inventory Items:");
                foreach (PreviousInventoryItem item in config.LastInventory)
                {
                    output.AppendLine($"Alias: {item.Alias}");
                    output.AppendLine($"Alias: {item.PrivateKeyEntry}");
                    foreach (string thumbprint in item.Thumbprints)
                    {
                        output.AppendLine($"Thumbprint: {thumbprint}");
                    }

                    output.AppendLine();        // Blank line
                }
            }

            // Certificate Store Properties
            output.AppendLine("Certificate Store Properties:");
            output.AppendLine($"Type: {config.CertificateStoreDetails.Type}");
            output.AppendLine($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");
            output.AppendLine($"Store Path: {config.CertificateStoreDetails.StorePath}");
            output.AppendLine($"Store Password: **************");

            output.AppendLine();        // Blank line

            JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            output.AppendLine("Cert Store Job Properties (Contains IIS and non-IIS Properties)");
            output.AppendLine($"SPN With Port: {jobProperties.SpnPortFlag}");
            output.AppendLine($"WinRm Protocol: {jobProperties.WinRmProtocol}");
            output.AppendLine($"WinRm Port: {jobProperties.WinRmPort}");
            output.AppendLine($"Server Username: {jobProperties.ServerUsername}");
            output.AppendLine($"Server Username: ***************");
            output.AppendLine($"Server Use SSL: {jobProperties.ServerUseSsl}");

            output.AppendLine();        // Blank line

            output.AppendLine("Job Configuration Properties:");
            output.AppendLine($"Job Cancelled: {config.JobCancelled}");
            output.AppendLine($"ServerError: {config.ServerError}");
            output.AppendLine($"Job History ID: {config.JobHistoryId}");
            output.AppendLine($"Request Status: {config.RequestStatus}");
            output.AppendLine($"Server Username: {config.ServerUsername}");
            output.AppendLine($"Server Username: ***************");
            output.AppendLine($"Use SSL: {config.UseSSL}");
            output.AppendLine($"Job Type ID: {config.JobTypeId}");
            output.AppendLine($"Job ID: {config.JobId}");
            output.AppendLine($"Capability: {config.Capability}");

            if (config.JobProperties != null)
            {
                output.AppendLine();        // Blank line
                output.AppendLine($"JSON Job Properties:");
                output.AppendLine($"Site Name: {config.JobProperties["SiteName"].ToString()}");
                output.AppendLine($"Port: {config.JobProperties["Port"].ToString()}");
                output.AppendLine($"Host Name: {config.JobProperties["HostName"]?.ToString()}");
                output.AppendLine($"Protocol: {config.JobProperties["Protocol"].ToString()}");
                output.AppendLine($"SniFlag: {config.JobProperties["SniFlag"].ToString()?[..1]}");
                output.AppendLine($"IP Address: {config.JobProperties["IPAddress"].ToString()}");
                output.AppendLine($"SAN: {config.JobProperties["SAN"]?.ToString()}");
            }

            return output.ToString();
        }
    }
}
