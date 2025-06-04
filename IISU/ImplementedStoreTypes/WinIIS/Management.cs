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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.PowerShell.Commands;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinIISUManagement";
        private ILogger _logger;

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

                _psHelper.Initialize();

                using (_psHelper)
                {
                    switch (_operationType)
                    {
                        case CertStoreOperationType.Add:
                            {
                                string certificateContents = config.JobCertificate.Contents;
                                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                                string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();

                                // Add Certificate to Cert Store
                                try
                                {
                                    IISBindingInfo bindingInfo = new IISBindingInfo(config.JobProperties);

                                    OrchestratorJobStatusJobResult psResult = OrchestratorJobStatusJobResult.Unknown;
                                    string failureMessage = "";
                                    
                                    string newThumbprint = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                                    _logger.LogTrace($"Completed adding the certificate to the store");
                                    _logger.LogTrace($"New thumbprint: {newThumbprint}");

                                    // Bind Certificate to IIS Site
                                    if (!string.IsNullOrEmpty(newThumbprint))
                                    {
                                        _logger.LogTrace("Returned after binding certificate to store");
                                        var results = WinIISBinding.BindCertificate(_psHelper, bindingInfo, newThumbprint, "", _storePath);
                                        if (results != null && results.Count > 0)
                                        {
                                            if (results[0] != null && results[0].Properties["Status"] != null)
                                            {
                                                string status = results[0].Properties["Status"]?.Value as string ?? string.Empty;
                                                int code = results[0].Properties["Code"]?.Value is int iCode ? iCode : -1;
                                                string step = results[0].Properties["Step"]?.Value as string ?? string.Empty;
                                                string message = results[0].Properties["Message"]?.Value as string ?? string.Empty;
                                                string errorMessage = results[0].Properties["ErrorMessage"]?.Value as string ?? string.Empty;

                                                switch (status)
                                                {
                                                    case "Success":
                                                        psResult = OrchestratorJobStatusJobResult.Success;
                                                        _logger.LogDebug($"PowerShell function New-KFIISSiteBinding returned successfully with Code: {code}, on Step: {step}");
                                                        break;
                                                    case "Skipped":
                                                        psResult = OrchestratorJobStatusJobResult.Failure;
                                                        failureMessage = ($"PowerShell function New-KFIISSiteBinding failed on step: {step} - message:\n {errorMessage}");
                                                        _logger.LogDebug(failureMessage);
                                                        break;
                                                    case "Warning":
                                                        psResult = OrchestratorJobStatusJobResult.Warning;
                                                        _logger.LogDebug($"PowerShell function New-KFIISSiteBinding returned with a Warning on step: {step} with code: {code} - message: {message}");
                                                        break;
                                                    case "Error":
                                                        psResult = OrchestratorJobStatusJobResult.Failure;
                                                        failureMessage = ($"PowerShell function New-KFIISSiteBinding failed on step: {step} with code: {code} - message: {errorMessage}");
                                                        _logger.LogDebug(failureMessage);
                                                        break;
                                                    default:
                                                        psResult = OrchestratorJobStatusJobResult.Unknown;
                                                        _logger.LogWarning("Unknown status returned from New-KFIISSiteBinding: " + status);
                                                        break;
                                                }
                                            }
                                            else
                                            {
                                                _logger.LogWarning("Unexpected object returned from PowerShell.");
                                                psResult = OrchestratorJobStatusJobResult.Unknown;
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogWarning("PowerShell script returned with no results.");
                                            psResult = OrchestratorJobStatusJobResult.Unknown;
                                        }

                                        complete = new JobResult
                                        {
                                            Result = psResult,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = failureMessage
                                        };
                                    }
                                    else
                                    {
                                        complete = new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Failure,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = $"No thumbprint was returned.  Unable to bind certificate to site: {bindingInfo.SiteName}."
                                        };                                    }
                                }
                                catch (Exception ex)
                                {
                                    return new JobResult
                                    {
                                        Result = OrchestratorJobStatusJobResult.Failure,
                                        JobHistoryId = _jobHistoryID,
                                        FailureMessage = ex.Message
                                    };
                                }

                                _logger.LogTrace($"Exiting the Adding of Certificate process.");

                                break;
                            }
                        case CertStoreOperationType.Remove:
                            {
                                // Removing a certificate involves two steps: UnBind the certificate, then delete the cert from the store

                                IISBindingInfo thisBinding = IISBindingInfo.ParseAliaseBindingString(config.JobCertificate.Alias);
                                string thumbprint = config.JobCertificate.Alias.Split(':')[0];
                                try
                                {
                                    if (WinIISBinding.UnBindCertificate(_psHelper, thisBinding))
                                    {
                                        // This function will only remove the certificate from the store if not used by any other sites
                                        RemoveIISCertificate(thisBinding.Thumbprint);

                                        complete = new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Success,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = ""
                                        };
                                    }
                                }
                                catch (Exception ex)
                                {
                                    return new JobResult
                                    {
                                        Result = OrchestratorJobStatusJobResult.Failure,
                                        JobHistoryId = _jobHistoryID,
                                        FailureMessage = ex.Message
                                    };
                                }

                                _logger.LogTrace($"Completed removing the certificate from the store");

                                break;
                            }
                    }
                }

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
            finally 
            { 
                _psHelper.Terminate();
                _logger.MethodExit(); 
            }
        }

        public string AddCertificate(string certificateContents, string privateKeyPassword, string cryptoProvider)
        {
            try
            {
                string newThumbprint = string.Empty;

                _logger.LogTrace("Attempting to execute PS function (Add-KFCertificateToStore)");

                // Mandatory parameters
                var parameters = new Dictionary<string, object>
                {
                    { "Base64Cert", certificateContents },
                    { "StoreName", _storePath },
                };

                // Optional parameters
                if (!string.IsNullOrEmpty(privateKeyPassword)) { parameters.Add("PrivateKeyPassword", privateKeyPassword); }
                if (!string.IsNullOrEmpty(cryptoProvider)) { parameters.Add("CryptoServiceProvider", cryptoProvider); }

                _results = _psHelper.ExecutePowerShell("Add-KFCertificateToStore", parameters);
                _logger.LogTrace("Returned from executing PS function (Add-KFCertificateToStore)");

                // This should return the thumbprint of the certificate
                if (_results != null && _results.Count > 0)
                {
                    newThumbprint = _results[0].ToString();
                    _logger.LogTrace($"Added certificate to store {_storePath}, returned with the thumbprint {newThumbprint}");
                }
                else
                {
                    _logger.LogTrace("No results were returned.  There could have been an error while adding the certificate.  Look in the trace logs for PowerShell information.");
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
        public void RemoveIISCertificate(string thumbprint)
        {
            _logger.LogTrace($"Attempting to remove thumbprint {thumbprint} from store {_storePath}");

            var parameters = new Dictionary<string, object>()
                    {
                        { "Thumbprint", thumbprint },
                        { "StoreName", _storePath }
                    };

            _psHelper.ExecutePowerShell("Remove-KFIISCertificateIfUnused", parameters);

        }

        public JobResult RemoveCertificateORIG(string thumbprint)
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
                        { "StorePath", _storePath }
                    };

                    _psHelper.ExecutePowerShell("Remove-KFCertificateFromStore", parameters);
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