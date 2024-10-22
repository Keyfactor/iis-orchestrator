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
// limitations under the License.using Keyfactor.Logging;

// Ignore Spelling: Keyfactor

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Management.Automation;
using Keyfactor.Logging;
using System.Collections.ObjectModel;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinCertManagement";
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
            _resolver= resolver;
        }

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            try
            {
                // Do some setup stuff
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
                    JobHistoryId = config.JobHistoryId,
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

                switch (_operationType)
                {
                    case CertStoreOperationType.Add:
                        {
                            string certificateContents = config.JobCertificate.Contents;
                            string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                            string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();

                            complete = AddCertificate(certificateContents, privateKeyPassword, _storePath, cryptoProvider);
                            _logger.LogTrace($"Completed adding the certificate to the store");

                            break;
                        }
                    case CertStoreOperationType.Remove:
                        {
                            string thumbprint = config.JobCertificate.Alias;

                            complete = RemoveCertificate(thumbprint, _storePath);
                            _logger.LogTrace($"Completed removing the certificate from the store");

                            break;
                        }
                }

                _logger.MethodExit();
                return complete;
            }

            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = failureMessage
                };
            }
        }

        public JobResult AddCertificate(string certificateContents, string privateKeyPassword, string storePath, string cryptoProvider)
        {
            try
            {
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
                        var thumbprint = _results[0].ToString();
                        _logger.LogTrace($"Added certificate to store {storePath}, returned with the thumbprint {thumbprint}");
                    }
                    else
                    {
                        _logger.LogTrace("No results were returned.  There could have been an error while adding the certificate.  Look in the trace logs for PowerShell informaiton.");
                    }
                    _psHelper.Terminate();
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = ""
                };

            }
            catch (Exception ex)
            {
                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = failureMessage
                };
            }
        }

        public JobResult RemoveCertificate(string thumbprint, string storePath)
        {
            try
            {
                using (_psHelper)
                {
                    _psHelper.Initialize();

                    _logger.LogTrace($"Attempting to remove thumbprint {thumbprint} from store {storePath}");
                    command = $"Remove-KFCertificateFromStore -Thumbprint '{thumbprint}' -StorePath '{storePath}'";
                    _psHelper.ExecuteFunction(command);
                    _logger.LogTrace("Returned from executing PS function (Remove-KFCertificateFromStore)");
                    
                    _psHelper.Terminate();
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = ""
                };
            }
            catch (Exception ex)
            {
                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = failureMessage
                };
            }
        }
    }
}
