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
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.PowerShell.Commands;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        private ILogger _logger;

        public string ExtensionName => string.Empty;

        private Runspace myRunspace;

        public Management(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            try
            {
                _logger = LogHandler.GetClassLogger<Management>();
                _logger.MethodEntry();

                _logger.LogTrace(JobConfigurationParser.ParseManagementJobConfiguration(config));

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool IncludePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = config.CertificateStoreDetails.ClientMachine;
                string storePath = config.CertificateStoreDetails.StorePath;
                long JobHistoryID = config.JobHistoryId;

                _logger.LogTrace($"Establishing runspace on client machine: {clientMachineName}");
                myRunspace = PSHelper.GetClientPsRunspace(protocol, clientMachineName, port, IncludePortInSPN, serverUserName, serverPassword);

                var complete = new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    FailureMessage =
                        "Invalid Management Operation"
                };

                switch (config.OperationType)
                {
                    case CertStoreOperationType.Add:
                        _logger.LogTrace("Entering Add...");

                        myRunspace.Open();
                        complete = PerformAddCertificate(config, serverUserName, serverPassword);
                        myRunspace.Close();

                        _logger.LogTrace("After Perform Addition...");
                        break;
                    case CertStoreOperationType.Remove:
                        _logger.LogTrace("Entering Remove...");

                        _logger.LogTrace("After PerformRemoval...");
                        myRunspace.Open();
                        complete = PerformRemoveCertificate(config, serverUserName, serverPassword);
                        myRunspace.Close();

                        _logger.LogTrace("After Perform Removal...");
                        break;
                }

                _logger.MethodExit();
                return complete;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                var failureMessage = $"Management job {config.OperationType} failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }

        private JobResult PerformAddCertificate(ManagementJobConfiguration config, string serverUsername, string serverPassword)
        {
            try
            {
#nullable enable
                string certificateContents = config.JobCertificate.Contents;
                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                string storePath = config.CertificateStoreDetails.StorePath;
                long jobNumber = config.JobHistoryId;
                string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();
#nullable disable

                // If a crypto provider was provided, check to see if it exists
                if (cryptoProvider != null)
                {
                    _logger.LogInformation($"Checking the server for the crypto provider: {cryptoProvider}");
                    if (!PSHelper.IsCSPFound(PSHelper.GetCSPList(myRunspace), cryptoProvider))
                    { throw new Exception($"The Crypto Profider: {cryptoProvider} was not found.  Please check the spelling and accuracy of the Crypto Provider Name provided.  If unsure which provider to use, leave the field blank and the default crypto provider will be used."); }
                }

                if (storePath != null)
                {
                    _logger.LogInformation($"Attempting to add IISU certificate to cert store: {storePath}");
                }

                ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, jobNumber);

                // This method is retired
                //JobResult result = manager.AddCertificate(certificateContents, privateKeyPassword, storePath);

                // Write the certificate contents to a temporary file on the remote computer, returning the filename.
                string filePath = manager.CreatePFXFile(certificateContents, privateKeyPassword);
                _logger.LogTrace($"{filePath} was created.");

                // Using certutil on the remote computer, import the pfx file using a supplied csp if any.
                JobResult result = manager.ImportPFXFile(filePath, privateKeyPassword, cryptoProvider, storePath);

                // Delete the temporary file
                manager.DeletePFXFile(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));

                if (result.Result == OrchestratorJobStatusJobResult.Success)
                {
                    // Bind to IIS
                    _logger.LogInformation("Attempting to bind certificate to website.");
                    ClientPSIIManager iisManager = new ClientPSIIManager(config, serverUsername, serverPassword);
                    result = iisManager.BindCertificate(manager.X509Cert);

                    // Provide logging information
                    if (result.Result == OrchestratorJobStatusJobResult.Success) { _logger.LogInformation("Certificate was successfully bound to the website.");  }
                    else { _logger.LogInformation("There was an issue while attempting to bind the certificate to the website.  Check the logs for more information."); }

                    return result;
                }
                else return result;
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage =
                        $"Management/Add {e.Message}"
                };
            }
        }

        private JobResult PerformRemoveCertificate(ManagementJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger.LogTrace("Before Remove Certificate...");

            string storePath = config.CertificateStoreDetails.StorePath;
            long jobNumber = config.JobHistoryId;

            // First we need to unbind the certificate from IIS before we remove it from the store
            ClientPSIIManager iisManager = new ClientPSIIManager(config, serverUsername, serverPassword);
            JobResult result = iisManager.UnBindCertificate();

            if (result.Result == OrchestratorJobStatusJobResult.Success)
            {
                ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, jobNumber);
                manager.RemoveCertificate(config.JobCertificate.Alias, storePath);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            else return result;
        }
    }
}