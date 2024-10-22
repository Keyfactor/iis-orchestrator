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
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinIISUManagement";
        private ILogger _logger;

        private string command = string.Empty;
        private PSHelper _psHelper;
        private Collection<PSObject>? _results = null;

        // Function wide config values
        private string _clientMachineName = string.Empty;
        private string _storePath = string.Empty;
        private long _jobHistoryID = 0;
        private CertStoreOperationType _operationType;

        //private Runspace myRunspace;

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

                try
                {
                    _logger.LogTrace(JobConfigurationParser.ParseManagementJobConfiguration(config));
                }
                catch (Exception e)
                {
                    _logger.LogTrace(e.Message);
                }

                var complete = new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    FailureMessage =
                        "Invalid Management Operation"
                };

                // Start parsing config information and establishing PS Session
                _jobHistoryID = config.JobHistoryId;
                _storePath = config.CertificateStoreDetails.StorePath;
                _clientMachineName = config.CertificateStoreDetails.ClientMachine;
                _operationType = config.OperationType;

                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                string protocol = jobProperties?.WinRmProtocol;
                string port = jobProperties?.WinRmPort;
                bool includePortInSPN = (bool)jobProperties?.SpnPortFlag;

                _psHelper = new(protocol, port, includePortInSPN, _clientMachineName, serverUserName, serverPassword);

                using (_psHelper)
                
                _psHelper.Initialize();

                switch (_operationType)
                {
                    case CertStoreOperationType.Add:
                        {
                            string certificateContents = config.JobCertificate.Contents;
                            string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                            string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();

                            // Add Certificate to Cert Store
                            using (_psHelper)
                            {

                                string newThumbprint = AddCertificate(certificateContents, privateKeyPassword, _storePath, cryptoProvider);
                                _logger.LogTrace($"Completed adding the certificate to the store");

                                // Bind Certificate to IIS Site
                                if (newThumbprint != null)
                                {

                                }

                            }


                            return new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Success,
                                JobHistoryId = _jobHistoryID,
                                FailureMessage = ""
                            };
                        }
                    case CertStoreOperationType.Remove:
                        {
                            string thumbprint = config.JobCertificate.Alias;

                            //complete = RemoveCertificate(thumbprint, _storePath);
                            _logger.LogTrace($"Completed removing the certificate from the store");

                            break;
                        }
                }

                _psHelper.Terminate();

                _logger.MethodExit();
                return complete;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex.Message);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = ex.Message
                };
            }
        }

        public string AddCertificate(string certificateContents, string privateKeyPassword, string storePath, string cryptoProvider)
        {
            try
            {
                string newThumbprint = string.Empty;

                using (_psHelper)
                {
                    _psHelper.Initialize();

                    _logger.LogTrace("Attempting to execute PS function (Add-KFCertificateToStore)");
                    command = $"Add-KFCertificateToStore -Base64Cert '{certificateContents}' -PrivateKeyPassword '{privateKeyPassword}' -StoreName '{storePath}' -CryptoServiceProvider '{cryptoProvider}'";
                    _results = _psHelper.ExecuteFunction(command);
                    _logger.LogTrace("Returned from executing PS function (Add-KFCertificateToStore)");

                    // This should return the thumbprint of the certificate
                    if (_results != null && _results.Count > 0)
                    {
                        newThumbprint= _results[0].ToString();
                        _logger.LogTrace($"Added certificate to store {storePath}, returned with the thumbprint {newThumbprint}");
                    }
                    else
                    {
                        _logger.LogTrace("No results were returned.  There could have been an error while adding the certificate.  Look in the trace logs for PowerShell informaiton.");
                    }
                    _psHelper.Terminate();
                }

                return newThumbprint;
            }
            catch (Exception ex)
            {
                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogError(failureMessage);

                throw new Exception (failureMessage);
            }
        }

        public void BindCertificate(string siteName, string protocol, string ipAddress, string port, string sniFlag, string storeName, string thumbPrint, string hostName = "")
        {
            /*
                function New-KFIISSiteBinding
             */

        }



//        private JobResult PerformAddCertificate(ManagementJobConfiguration config, string serverUsername, string serverPassword)
//        {
//            try
//            {
//#nullable enable
//                string certificateContents = config.JobCertificate.Contents;
//                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
//                string storePath = config.CertificateStoreDetails.StorePath;
//                long jobNumber = config.JobHistoryId;
//                string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();
//#nullable disable

//                // If a crypto provider was provided, check to see if it exists
//                if (cryptoProvider != null)
//                {
//                    _logger.LogInformation($"Checking the server for the crypto provider: {cryptoProvider}");
//                    if (!PSHelper.IsCSPFound(PSHelper.GetCSPList(myRunspace), cryptoProvider))
//                    { throw new Exception($"The Crypto Profider: {cryptoProvider} was not found.  Please check the spelling and accuracy of the Crypto Provider Name provided.  If unsure which provider to use, leave the field blank and the default crypto provider will be used."); }
//                }

//                if (storePath != null)
//                {
//                    _logger.LogInformation($"Attempting to add IISU certificate to cert store: {storePath}");
//                }

//                ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, jobNumber);

//                // This method is retired
//                //JobResult result = manager.AddCertificate(certificateContents, privateKeyPassword, storePath);

//                // Write the certificate contents to a temporary file on the remote computer, returning the filename.
//                string filePath = manager.CreatePFXFile(certificateContents, privateKeyPassword);
//                _logger.LogTrace($"{filePath} was created.");

//                // Using certutil on the remote computer, import the pfx file using a supplied csp if any.
//                JobResult result = manager.ImportPFXFile(filePath, privateKeyPassword, cryptoProvider, storePath);

//                // Delete the temporary file
//                manager.DeletePFXFile(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));

//                if (result.Result == OrchestratorJobStatusJobResult.Success)
//                {
//                    // Bind to IIS
//                    _logger.LogInformation("Attempting to bind certificate to website.");
//                    ClientPSIIManager iisManager = new ClientPSIIManager(config, serverUsername, serverPassword);
//                    result = iisManager.BindCertificate(manager.X509Cert);

//                    // Provide logging information
//                    if (result.Result == OrchestratorJobStatusJobResult.Success) { _logger.LogInformation("Certificate was successfully bound to the website.");  }
//                    else { _logger.LogInformation("There was an issue while attempting to bind the certificate to the website.  Check the logs for more information."); }

//                    return result;
//                }
//                else return result;
//            }
//            catch (Exception e)
//            {
//                return new JobResult
//                {
//                    Result = OrchestratorJobStatusJobResult.Failure,
//                    JobHistoryId = config.JobHistoryId,
//                    FailureMessage =
//                        $"Management/Add {e.Message}"
//                };
//            }
//        }

//        private JobResult PerformRemoveCertificate(ManagementJobConfiguration config, string serverUsername, string serverPassword)
//        {
//            _logger.LogTrace("Before Remove Certificate...");

//            string storePath = config.CertificateStoreDetails.StorePath;
//            long jobNumber = config.JobHistoryId;

//            // First we need to unbind the certificate from IIS before we remove it from the store
//            ClientPSIIManager iisManager = new ClientPSIIManager(config, serverUsername, serverPassword);
//            JobResult result = iisManager.UnBindCertificate();

//            if (result.Result == OrchestratorJobStatusJobResult.Success)
//            {
//                ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, jobNumber);
//                manager.RemoveCertificate(config.JobCertificate.Alias, storePath);

//                return new JobResult
//                {
//                    Result = OrchestratorJobStatusJobResult.Success,
//                    JobHistoryId = config.JobHistoryId,
//                    FailureMessage = ""
//                };
//            }
//            else return result;
//        }
    }
}