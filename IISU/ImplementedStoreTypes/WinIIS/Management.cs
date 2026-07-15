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

// Ignore Spelling: Keyfactor IISU Crypto
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private Collection<PSObject>? _results = null;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

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
                string jeaEndpoint = jobProperties?.JEAEndpointName ?? "";
                string alias = config.JobCertificate?.Alias?.Split(':').FirstOrDefault() ?? string.Empty;  // Thumbprint is first part of the alias

                // Assign the binding information
                IISBindingInfo bindingInfo = new IISBindingInfo(config.JobProperties);

                _psHelper = new(protocol, port, includePortInSPN, _clientMachineName, serverUserName, serverPassword, jeaEndpoint: jeaEndpoint);

                _psHelper.Initialize();

                using (_psHelper)
                {
                    switch (_operationType)
                    {
                        case CertStoreOperationType.Add:
                            {
                                _logger.LogTrace($"Beginning the Adding of Certificate process.");

                                string certificateContents = config.JobCertificate.Contents;
                                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
                                string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

                                // Add Certificate to Cert Store
                                try
                                {

                                    OrchestratorJobStatusJobResult psResult = OrchestratorJobStatusJobResult.Unknown;
                                    string failureMessage = "";

                                    ResultObject addResult = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                                    _logger.LogTrace($"Completed adding the certificate to the store. Status={addResult.Status}, Code={addResult.Code}, Step={addResult.Step}");

                                    if (!addResult.IsSuccess)
                                    {
                                        string detail = !string.IsNullOrEmpty(addResult.ErrorMessage)
                                            ? addResult.ErrorMessage
                                            : addResult.Message;

                                        string addFailureMessage =
                                            $"Add certificate to store '{_storePath}' failed at step '{addResult.Step}' (code {addResult.Code}): {detail}";

                                        _logger.LogError(addFailureMessage);

                                        complete = new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Failure,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = addFailureMessage
                                        };
                                        break;
                                    }

                                    string newThumbprint = addResult.Thumbprint;
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

                                        // Only is the binding returns successful, check of original cert is still bound to any site, if not remove it from the store
                                        if (psResult == OrchestratorJobStatusJobResult.Success && !string.IsNullOrEmpty(alias))
                                        {
                                            _logger.LogTrace("Attempting to remove original certificate from store if it is no longer bound to any site.");
                                            var cleanupResult = RemoveIISCertificate(alias);
                                            if (cleanupResult != null)
                                            {
                                                // Binding already succeeded — a cleanup failure downgrades to a
                                                // Warning rather than masking the successful bind as a Failure.
                                                psResult = cleanupResult.Result;
                                                failureMessage = cleanupResult.FailureMessage;
                                            }
                                            _logger.LogTrace("Returned from removing cert if not used.");
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
                                            FailureMessage = $"Add-KeyfactorCertificate reported Success but did not return a thumbprint. Unable to bind certificate to site: {bindingInfo.SiteName}."
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
                                        // This function will only remove the certificate from the store if not used by any other sites.
                                        // The unbind already succeeded — a cleanup failure downgrades to a Warning
                                        // rather than masking the successful unbind as a Failure.
                                        var cleanupResult = RemoveIISCertificate(thisBinding.Thumbprint);

                                        complete = cleanupResult ?? new JobResult
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
                if (_psHelper != null) _psHelper.Terminate();
                _logger.MethodExit(); 
            }
        }

        public ResultObject AddCertificate(string certificateContents, string privateKeyPassword, string cryptoProvider)
        {
            try
            {
                _logger.LogTrace("Attempting to execute PS function (Add-KeyfactorCertificate)");

                // Mandatory parameters
                var parameters = new Dictionary<string, object>
                {
                    { "Base64Cert", certificateContents },
                    { "StoreName", _storePath },
                };

                // Optional parameters
                if (!string.IsNullOrEmpty(privateKeyPassword)) { parameters.Add("PrivateKeyPassword", privateKeyPassword); }
                if (!string.IsNullOrEmpty(cryptoProvider)) { parameters.Add("CryptoServiceProvider", cryptoProvider); }

                _results = _psHelper.ExecutePowerShell("Add-KeyfactorCertificate", parameters);
                _logger.LogTrace("Returned from executing PS function (Add-KeyfactorCertificate)");

                ResultObject result = ResultObject.FromPSResults(_results);
                _logger.LogTrace($"Add-KeyfactorCertificate returned Status={result.Status}, Code={result.Code}, Step={result.Step}, Thumbprint='{result.Thumbprint}'");

                if (!result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    _logger.LogWarning($"Add-KeyfactorCertificate error: {result.ErrorMessage}");
                }

                return result;
            }
            catch (Exception ex)        
            {
                var failureMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: '{LogHandler.FlattenException(ex)}'";
                var niceMessage = $"Management job {_operationType} failed on Store '{_storePath}' on server '{_clientMachineName}' with error: {ex.Message}";
                _logger.LogError(failureMessage);

                throw new Exception (niceMessage);
            }
}
        /// <summary>
        /// Attempts to remove a certificate from the store if it is no longer bound to any IIS site.
        /// Returns null when there is nothing to report (removed, skipped because still in use, or not
        /// found). Returns a Warning JobResult when the cleanup itself failed, since at the point this is
        /// called the primary bind/unbind operation has already succeeded and a cleanup failure should
        /// not be reported to the orchestrator as if the whole job failed.
        /// </summary>
        public JobResult RemoveIISCertificate(string thumbprint)
        {
            _logger.LogTrace($"Attempting to remove thumbprint {thumbprint} from store {_storePath}");

            var parameters = new Dictionary<string, object>()
                    {
                        { "Thumbprint", thumbprint },
                        { "StoreName", _storePath }
                    };

            try
            {
                var results = _psHelper.ExecutePowerShell("Remove-KeyfactorIISCertificateIfUnused", parameters);
                ResultObject result = ResultObject.FromPSResults(results);

                _logger.LogTrace($"Remove-KeyfactorIISCertificateIfUnused returned Status={result.Status}, Code={result.Code}, Step={result.Step}");

                if (string.Equals(result.Status, ResultObject.StatusError, StringComparison.OrdinalIgnoreCase))
                {
                    var warningMessage = $"Certificate '{thumbprint}' could not be removed from store '{_storePath}': {result.ErrorMessage}";
                    _logger.LogWarning(warningMessage);

                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Warning,
                        JobHistoryId = _jobHistoryID,
                        FailureMessage = warningMessage
                    };
                }

                // Success or Skipped (still in use elsewhere / not found) are both benign outcomes.
                return null;
            }
            catch (Exception ex)
            {
                var warningMessage = $"Certificate '{thumbprint}' could not be removed from store '{_storePath}': {ex.Message}";
                _logger.LogWarning(warningMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Warning,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = warningMessage
                };
            }
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