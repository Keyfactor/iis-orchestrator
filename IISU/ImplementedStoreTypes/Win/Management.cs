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

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Management.Automation.Runspaces;
using System.Management.Automation;
using System.Net;
using Keyfactor.Logging;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        private ILogger _logger;

        public string ExtensionName => string.Empty;

        private Runspace myRunspace;

        private string _thumbprint = string.Empty;

        public Management()
        {
        }

        public Management(IPAMSecretResolver resolver)
        {
            _resolver= resolver;
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
                myRunspace = PsHelper.GetClientPsRunspace(protocol, clientMachineName, port, IncludePortInSPN, serverUserName, serverPassword);

                var complete = new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage =
                        "Invalid Management Operation"
                };

                switch (config.OperationType)
                {
                    case CertStoreOperationType.Add:
                        {
                            myRunspace.Open();
                            _logger.LogTrace("runSpace Opened");

                            complete = performAddition(config);

                            myRunspace.Close();
                            _logger.LogTrace($"RunSpace was closed...");

                            break;
                        }
                    case CertStoreOperationType.Remove:
                        {
                            myRunspace.Open();
                            _logger.LogTrace("runSpace Opened");

                            complete = performRemove(config);

                            myRunspace.Close();
                            _logger.LogTrace($"RunSpace was closed...");

                            break;
                        }
                }

                return complete;
            }

            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in Management.PerformManagement: {e.Message}");
                throw;
            }
        }

        private JobResult performAddition(ManagementJobConfiguration config)
        {
            try
            {
                string certificateContents = config.JobCertificate.Contents;
                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                string storePath = config.CertificateStoreDetails.StorePath;
                long jobNumber = config.JobHistoryId;

                // Setup a new connection to the client machine
                //var connectionInfo = new WSManConnectionInfo(new Uri($"{certStoreDetails?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{certStoreDetails?.WinRmPort}/wsman"));
                //_logger.LogTrace($"WinRm URL: {certStoreDetails?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{certStoreDetails?.WinRmPort}/wsman");

                if (storePath != null)
                {
                    _logger.LogTrace($"Attempting to get licenses from cert path: {storePath})");
                    
                    ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, jobNumber);
                    JobResult result = manager.AddCertificate(certificateContents, privateKeyPassword, storePath);
                    
                    _logger.LogTrace($"Certificate was successfully added to cert store: {storePath})");
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
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

        private JobResult performRemove(ManagementJobConfiguration config)
        {
            try
            {
                _logger.LogTrace($"Removing Certificate with Alias: {config.JobCertificate.Alias}");
                ClientPSCertStoreManager manager = new ClientPSCertStoreManager(_logger, myRunspace, config.JobHistoryId);
                manager.RemoveCertificate(config.JobCertificate.Alias, config.CertificateStoreDetails.StorePath);
                _logger.LogTrace($"Removed Certificate with Alias: {config.JobCertificate.Alias}");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = $"Error Occurred while attempting to remove certificate: {LogHandler.FlattenException(e)}"
                };
            }
        }
    }
}
