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
// limitations under the License.using Keyfactor.Logging;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinAdfs;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinAdfs
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinAdfsManagement";
        private ILogger _logger;

        private PSHelper _psHelper;

        // Function wide config values
        private string _clientMachineName = string.Empty;
        private string _storePath = string.Empty;
        private long _jobHistoryID = 0;
        private CertStoreOperationType _operationType;

        public Management(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
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
                    FailureMessage = "Invalid Management Operation"
                };

                // Start parsing config information and establishing PS Session
                _jobHistoryID = config.JobHistoryId;
                _storePath = config.CertificateStoreDetails.StorePath;
                _clientMachineName = config.CertificateStoreDetails.ClientMachine;
                _operationType = config.OperationType;

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                string protocol = jobProperties?.WinRmProtocol;
                string port = jobProperties?.WinRmPort;
                bool includePortInSPN = (bool)jobProperties?.SpnPortFlag;

                _psHelper = new(protocol, port, includePortInSPN, _clientMachineName, serverUserName, serverPassword, true);
                _psHelper.Initialize();

                switch (_operationType)
                {
                    case CertStoreOperationType.Add:
                        {
                            string certificateContents = config.JobCertificate.Contents;
                            string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;

                            string pfxPath = Certificate.Utilities.WriteCertificateToTempPfx(certificateContents);
                            using (var rotationManager = new AdfsCertificateRotationManager(
                                _psHelper,           // Primary PSHelper connection
                                protocol,            // For creating direct connections to other nodes
                                port,                // For creating direct connections to other nodes
                                includePortInSPN,    // For creating direct connections to other nodes
                                serverUserName,      // For creating direct connections to other nodes
                                serverPassword))
                            {
                                var result = rotationManager.RotateServiceCommunicationCertificate(pfxPath, privateKeyPassword);
                                if (result.Success)
                                {
                                    AdfsCertificateRotationManager.UpdateFarmCertificateSettings(result.Thumbprint, _psHelper);
                                }

                                _logger.LogInformation($"Adfs Service Communication Certificate rotation result: {(result.Success ? "SUCCESSFUL" : "FAILED")}");
                                _logger.LogInformation(result.Message);

                                if (result.Success)
                                {
                                    complete = new JobResult
                                    {
                                        Result = OrchestratorJobStatusJobResult.Success,
                                        JobHistoryId = _jobHistoryID,
                                        FailureMessage = $"Adfs Service Communication Certificate rotated successfully to thumbprint: {result.Thumbprint}"
                                    };
                                }
                                else
                                {
                                    complete = new JobResult
                                    {
                                        Result = OrchestratorJobStatusJobResult.Failure,
                                        JobHistoryId = _jobHistoryID,
                                        FailureMessage = $"Adfs Service Communication Certificate rotation failed. {result.Message}"
                                    };
                                }
                            }

                            //complete = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                            Certificate.Utilities.CleanupTempCertificate(pfxPath);
                            _logger.LogTrace($"Completed adding the certificate to the store");

                            break;
                        }
                    default:
                        {
                            _logger.LogWarning($"Management job of type {_operationType} is not supported in WinAdfs store.");
                            break;
                        }
                }

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
            finally
            {
                if (_psHelper != null) _psHelper.Terminate();
                _logger.MethodExit();
            }

        }
    }
}
