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
using System.IO;
using System.Management.Automation;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinIIS;
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
                                    string newThumbprint = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                                    _logger.LogTrace($"Completed adding the certificate to the store");

                                    // Bind Certificate to IIS Site
                                    if (newThumbprint != null)
                                    {
                                        IISBindingInfo bindingInfo = new IISBindingInfo(config.JobProperties);
                                        BindCertificate(bindingInfo, newThumbprint);

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

                                _logger.LogTrace($"Completed adding and binding the certificate to the store");

                                break;
                            }
                        case CertStoreOperationType.Remove:
                            {
                                // Removing a certificate involves two steps: UnBind the certificate, then delete the cert from the store

                                string thumbprint = config.JobCertificate.Alias.Split(':')[0];
                                try
                                {
                                    if (UnBindCertificate(new IISBindingInfo(config.JobProperties)))
                                    {
                                        complete = RemoveCertificate(thumbprint);
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

        public void BindCertificate(IISBindingInfo bindingInfo, string thumbprint)
        {
            _logger.LogTrace("Attempting to bind and execute PS function (New-KFIISSiteBinding)");
                
            // Manditory parameters
            var parameters = new Dictionary<string, object>
            {
                { "Thumbprint", thumbprint },
                { "WebSite", bindingInfo.SiteName },
                { "Protocol", bindingInfo.Protocol },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
                { "SNIFlag", bindingInfo.SniFlag },
                { "StoreName", _storePath },
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            _results = _psHelper.ExecutePowerShell("New-KFIISSiteBinding", parameters);
            _logger.LogTrace("Returned from executing PS function (Add-KFCertificateToStore)");

            // This should return the thumbprint of the certificate
            if (_results != null && _results.Count > 0)
            {
                _logger.LogTrace($"Bound certificate with the thumbprint: '{thumbprint}' to site: '{bindingInfo.SiteName}'.");
            }
            else
            {
                _logger.LogTrace("No results were returned.  There could have been an error while adding the certificate.  Look in the trace logs for PowerShell informaiton.");
            }
        }

        public bool UnBindCertificate(IISBindingInfo bindingInfo)
        {
            _logger.LogTrace("Attempting to UnBind and execute PS function (Remove-KFIISBinding)");

            // Manditory parameters
            var parameters = new Dictionary<string, object>
            {
                { "SiteName", bindingInfo.SiteName },
                { "IPAddress", bindingInfo.IPAddress },
                { "Port", bindingInfo.Port },
            };

            // Optional parameters
            if (!string.IsNullOrEmpty(bindingInfo.HostName)) { parameters.Add("HostName", bindingInfo.HostName); }

            try
            {
                _results = _psHelper.ExecutePowerShell("Remove-KFIISBinding", parameters);
                _logger.LogTrace("Returned from executing PS function (Remove-KFIISBinding)");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}