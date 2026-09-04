// Copyright 2026 Keyfactor
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

// Ignore Spelling: Keyfactor Ldap Ldaps

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Management.Automation;
using Keyfactor.Logging;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinLdap
{
    // Add/Remove only for the initial release - no ReEnrollment/Discovery, matching the
    // constrained-scope precedent set by WinAdfs. See docsource/winldap.md.
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinLdapManagement";
        private ILogger _logger;

        private PSHelper _psHelper;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private Collection<PSObject>? _results = null;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

        // Function wide config values
        private string _clientMachineName = string.Empty;
        private string _storePath = string.Empty;
        private long _jobHistoryID = 0;
        private CertStoreOperationType _operationType;
        private bool _restartService = false;

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
                string jeaEndpoint = jobProperties?.JEAEndpointName ?? "";
                _restartService = jobProperties?.RestartService ?? false;

                _psHelper = new(protocol, port, includePortInSPN, _clientMachineName, serverUserName, serverPassword, jeaEndpoint: jeaEndpoint);

                switch (_operationType)
                {
                    case CertStoreOperationType.Add:
                        {
                            string certificateContents = config.JobCertificate.Contents;
                            string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
                            string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

                            complete = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                            _logger.LogTrace($"Completed adding the certificate to the NTDS (LDAPS) certificate store");

                            break;
                        }
                    case CertStoreOperationType.Remove:
                        {
                            string thumbprint = config.JobCertificate.Alias;

                            complete = RemoveCertificate(thumbprint);
                            _logger.LogTrace($"Completed removing the certificate from the NTDS (LDAPS) certificate store");

                            break;
                        }
                }

                _logger.MethodExit();
                return complete;
            }

            catch (Exception ex)
            {
                _logger.LogTrace(LogHandler.FlattenException(ex));

                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{ex.Message}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = failureMessage
                };
            }
        }

        public JobResult AddCertificate(string certificateContents, string privateKeyPassword, string cryptoProvider)
        {
            try
            {
                using (_psHelper)
                {
                    _psHelper.Initialize();

                    _logger.LogTrace("Attempting to execute PS function (Add-KeyfactorLdapsCertificate)");

                    // Mandatory parameters
                    var parameters = new Dictionary<string, object>
                    {
                        { "Base64Cert", certificateContents },
                        { "StoreName", _storePath },
                        { "RestartService", _restartService },
                    };

                    // Optional parameters
                    if (!string.IsNullOrEmpty(privateKeyPassword)) { parameters.Add("PrivateKeyPassword", privateKeyPassword); }
                    if (!string.IsNullOrEmpty(cryptoProvider)) { parameters.Add("CryptoServiceProvider", cryptoProvider); }

                    _results = _psHelper.ExecutePowerShell("Add-KeyfactorLdapsCertificate", parameters);
                    _logger.LogTrace("Returned from executing PS function (Add-KeyfactorLdapsCertificate)");

                    ResultObject addResult = ResultObject.FromPSResults(_results);
                    _logger.LogTrace($"Add-KeyfactorLdapsCertificate returned Status={addResult.Status}, Code={addResult.Code}, Step={addResult.Step}, Thumbprint='{addResult.Thumbprint}'");

                    _psHelper.Terminate();

                    if (!addResult.IsSuccess)
                    {
                        string detail = !string.IsNullOrEmpty(addResult.ErrorMessage)
                            ? addResult.ErrorMessage
                            : addResult.Message;

                        string failureMessage =
                            $"Add certificate to store '{_storePath}' failed at step '{addResult.Step}' (code {addResult.Code}): {detail}";

                        _logger.LogWarning(failureMessage);

                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = _jobHistoryID,
                            FailureMessage = failureMessage
                        };
                    }

                    _logger.LogTrace($"Added certificate to store {_storePath}, thumbprint {addResult.Thumbprint}");
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
                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{ex.Message}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = failureMessage
                };
            }
        }

        public JobResult RemoveCertificate(string thumbprint)
        {
            try
            {
                using (_psHelper)
                {
                    _psHelper.Initialize();

                    _logger.LogTrace($"Attempting to remove thumbprint {thumbprint} from store {_storePath}");

                    var parameters = new Dictionary<string, object>()
                    {
                        { "Thumbprint", thumbprint },
                        { "StoreName", _storePath }
                    };

                    _results = _psHelper.ExecutePowerShell("Remove-KeyfactorLdapsCertificate", parameters);
                    _logger.LogTrace("Returned from executing PS function (Remove-KeyfactorLdapsCertificate)");

                    ResultObject removeResult = ResultObject.FromPSResults(_results);

                    _psHelper.Terminate();

                    if (!removeResult.IsSuccess)
                    {
                        string detail = !string.IsNullOrEmpty(removeResult.ErrorMessage)
                            ? removeResult.ErrorMessage
                            : removeResult.Message;

                        string failureMessage =
                            $"Remove certificate from store '{_storePath}' failed at step '{removeResult.Step}' (code {removeResult.Code}): {detail}";

                        _logger.LogWarning(failureMessage);

                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = _jobHistoryID,
                            FailureMessage = failureMessage
                        };
                    }
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
