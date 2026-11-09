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

// Ignore Spelling: Keyfactor NetSH Sslcert

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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinNetSH
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinNetSHManagement";
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
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = "Invalid Management Operation"
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

                _psHelper = new(protocol, port, includePortInSPN, _clientMachineName, serverUserName, serverPassword, jeaEndpoint: jeaEndpoint, adminPrivilegesRequired: true);
                _psHelper.Initialize();

                using (_psHelper)
                {
                    switch (_operationType)
                    {
                        case CertStoreOperationType.Add:
                            {
                                _logger.LogTrace("Beginning the Adding of Certificate process.");

                                string certificateContents = config.JobCertificate.Contents;
                                string privateKeyPassword = config.JobCertificate.PrivateKeyPassword;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
                                string? cryptoProvider = config.JobProperties["ProviderName"]?.ToString();
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

                                // Thumbprint of the certificate this Add is replacing (Command populates the
                                // outgoing Alias with the certificate currently bound at this ipport/hostnameport
                                // when this is a renewal) - matches the convention WinIIS uses for post-bind cleanup.
                                string oldThumbprint = config.JobCertificate?.Alias?.Split(':').FirstOrDefault() ?? string.Empty;

                                NetSHBindingInfo bindingInfo = new NetSHBindingInfo(config.JobProperties);

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

                                    if (string.IsNullOrEmpty(newThumbprint))
                                    {
                                        complete = new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Failure,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = $"Add-KeyfactorCertificate reported Success but did not return a thumbprint. Unable to bind certificate to {bindingInfo.IPAddress}:{bindingInfo.Port}."
                                        };
                                        break;
                                    }

                                    ResultObject bindResult = WinNetSHBinding.BindCertificate(_psHelper, bindingInfo, newThumbprint, _storePath);
                                    _logger.LogTrace($"New-KeyfactorNetSHBinding returned Status={bindResult.Status}, Code={bindResult.Code}, Step={bindResult.Step}");

                                    switch (bindResult.Status)
                                    {
                                        case "Success":
                                            psResult = OrchestratorJobStatusJobResult.Success;
                                            break;
                                        case "Skipped":
                                            psResult = OrchestratorJobStatusJobResult.Failure;
                                            failureMessage = $"PowerShell function New-KeyfactorNetSHBinding failed on step: {bindResult.Step} - message:\n {bindResult.ErrorMessage}";
                                            break;
                                        case "Warning":
                                            psResult = OrchestratorJobStatusJobResult.Warning;
                                            failureMessage = bindResult.Message;
                                            break;
                                        case "Error":
                                            psResult = OrchestratorJobStatusJobResult.Failure;
                                            failureMessage = $"PowerShell function New-KeyfactorNetSHBinding failed on step: {bindResult.Step} with code: {bindResult.Code} - message: {bindResult.ErrorMessage}";
                                            break;
                                        default:
                                            psResult = OrchestratorJobStatusJobResult.Unknown;
                                            _logger.LogWarning("Unknown status returned from New-KeyfactorNetSHBinding: " + bindResult.Status);
                                            break;
                                    }

                                    // Surface the resolved AppId back to Command whenever it was not the value the
                                    // caller supplied (i.e. it was reused from an existing binding or freshly
                                    // generated) - see the AppId resolution behavior in docsource/winnetsh.md.
                                    if (psResult == OrchestratorJobStatusJobResult.Success &&
                                        bindResult.Details != null &&
                                        bindResult.Details.TryGetValue("AppId", out var resolvedAppIdObj) &&
                                        bindResult.Details.TryGetValue("AppIdSource", out var appIdSourceObj) &&
                                        !string.Equals(appIdSourceObj?.ToString(), "Supplied", StringComparison.OrdinalIgnoreCase))
                                    {
                                        failureMessage = $"Certificate bound successfully. AppId was {appIdSourceObj?.ToString()?.ToLowerInvariant()} for this binding: {resolvedAppIdObj}";
                                        _logger.LogInformation(failureMessage);
                                    }

                                    // Only clean up the old certificate if the new binding succeeded and this was
                                    // actually a renewal (an old thumbprint was present in the outgoing Alias).
                                    if (psResult == OrchestratorJobStatusJobResult.Success && !string.IsNullOrEmpty(oldThumbprint) &&
                                        !string.Equals(oldThumbprint, newThumbprint, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogTrace("Attempting to remove the superseded certificate from the store if it is no longer bound to any binding.");
                                        var cleanupResult = RemoveNetSHCertificate(oldThumbprint);
                                        if (cleanupResult != null)
                                        {
                                            // Binding already succeeded - a cleanup failure downgrades to a
                                            // Warning rather than masking the successful bind as a Failure.
                                            psResult = cleanupResult.Result;
                                            failureMessage = cleanupResult.FailureMessage;
                                        }
                                    }

                                    complete = new JobResult
                                    {
                                        Result = psResult,
                                        JobHistoryId = _jobHistoryID,
                                        FailureMessage = failureMessage
                                    };
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

                                _logger.LogTrace("Exiting the Adding of Certificate process.");

                                break;
                            }
                        case CertStoreOperationType.Remove:
                            {
                                // Removing a certificate involves two steps: unbind it, then delete the cert from
                                // the store if it's no longer used by any other binding.
                                NetSHBindingInfo thisBinding = NetSHBindingInfo.ParseAliasBindingString(config.JobCertificate.Alias);

                                try
                                {
                                    if (WinNetSHBinding.UnBindCertificate(_psHelper, thisBinding))
                                    {
                                        var cleanupResult = RemoveNetSHCertificate(thisBinding.Thumbprint);

                                        complete = cleanupResult ?? new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Success,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = ""
                                        };
                                    }
                                    else
                                    {
                                        complete = new JobResult
                                        {
                                            Result = OrchestratorJobStatusJobResult.Failure,
                                            JobHistoryId = _jobHistoryID,
                                            FailureMessage = $"Failed to remove the netsh sslcert binding for {thisBinding.IPAddress}:{thisBinding.Port}."
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

                                _logger.LogTrace("Completed removing the certificate from the store");

                                break;
                            }
                    }
                }

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

                throw new Exception(niceMessage);
            }
        }

        /// <summary>
        /// Attempts to remove a certificate from the store if it is no longer used by any netsh
        /// sslcert binding. Returns null when there is nothing to report (removed, skipped because
        /// still in use, or not found). Returns a Warning JobResult when the cleanup itself failed,
        /// since at the point this is called the primary bind/unbind operation has already succeeded
        /// and a cleanup failure should not be reported to the orchestrator as if the whole job failed.
        /// </summary>
        public JobResult RemoveNetSHCertificate(string thumbprint)
        {
            _logger.LogTrace($"Attempting to remove thumbprint {thumbprint} from store {_storePath}");

            var parameters = new Dictionary<string, object>()
            {
                { "Thumbprint", thumbprint },
                { "StoreName", _storePath }
            };

            try
            {
                var results = _psHelper.ExecutePowerShell("Remove-KeyfactorNetSHCertificateIfUnused", parameters);
                ResultObject result = ResultObject.FromPSResults(results);

                _logger.LogTrace($"Remove-KeyfactorNetSHCertificateIfUnused returned Status={result.Status}, Code={result.Code}, Step={result.Step}");

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
    }
}
