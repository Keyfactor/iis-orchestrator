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

// Ignore Spelling: thumbprint

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Text;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Numerics;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql
{
    public class Management : WinCertJobTypeBase, IManagementJobExtension
    {
        public string ExtensionName => "WinSqlManagement";
        private ILogger _logger;

        private PSHelper _psHelper;
        private Collection<PSObject>? _results = null;

        // Function wide config values
        private string _clientMachineName = string.Empty;
        private string _storePath = string.Empty;
        private long _jobHistoryID = 0;
        private CertStoreOperationType _operationType;

        private string RenewalThumbprint = string.Empty;
        private string SQLInstanceNames = "MSSQLSERVER";
        private bool RestartSQLService = false;

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

                string serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                string serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                string protocol = jobProperties?.WinRmProtocol;
                string port = jobProperties?.WinRmPort;
                bool includePortInSPN = (bool)jobProperties?.SpnPortFlag;
                
                RestartSQLService = jobProperties.RestartService;

                if (config.JobProperties.ContainsKey("InstanceName"))
                {
                    SQLInstanceNames = config.JobProperties["InstanceName"]?.ToString() ?? "MSSQLSERVER";
                }

                if (config.JobProperties.ContainsKey("RenewalThumbprint"))
                {
                    RenewalThumbprint = config.JobProperties["RenewalThumbprint"]?.ToString();
                }

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
                                string cryptoProvider = config.JobProperties["ProviderName"]?.ToString() ?? string.Empty;

                                // Add Certificate to Cert Store
                                try
                                {
                                    string newThumbprint = AddCertificate(certificateContents, privateKeyPassword, cryptoProvider);
                                    _logger.LogTrace($"Completed adding the certificate to the store");

                                    // Bind Certificate to SQL Instance
                                    if (newThumbprint != null)
                                    {
                                        complete = BindSQLCertificate(newThumbprint, RenewalThumbprint);
                                        // Check the RenewalThumbprint.  If there is a value, this is a renewal
                                        if (config.JobProperties.ContainsKey("RenewalThumbprint"))
                                        {
                                            // This is a renewal.
                                            // Check if there is an existing certificate.  If there is, replace it with the new one.
                                            string renewalThumbprint = config.JobProperties["RenewalThumbprint"]?.ToString() ?? string.Empty;
                                        }
                                        else
                                        {
                                            // This is a new certificate - just bind it.
                                        }

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
                                try
                                {
                                    // Unbind the certificates
                                    if (UnBindSQLCertificate().Result == OrchestratorJobStatusJobResult.Success)
                                    {
                                        // Remove the certificate from the cert store
                                        complete = RemoveCertificate(config.JobCertificate.Alias);
                                        _logger.LogTrace($"Completed removing the certificate from the store");

                                        break;
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

                                _logger.LogTrace($"Completed unbinding and removing the certificate from the store");

                                break;

                                string thumbprint = config.JobCertificate.Alias;

                                complete = RemoveCertificate(thumbprint);
                                _logger.LogTrace($"Completed removing the certificate from the store");

                                break;
                            }
                    }
                }

                return complete;

                //switch (config.OperationType)
                //{
                //    case CertStoreOperationType.Add:
                //        _logger.LogTrace("Entering Add...");
                //        myRunspace.Open();
                //        complete = PerformAddCertificate(config, serverUserName, serverPassword);
                //        myRunspace.Close();
                //        _logger.LogTrace("After Perform Addition...");
                //        break;
                //    case CertStoreOperationType.Remove:
                //        _logger.LogTrace("Entering Remove...");
                //        _logger.LogTrace("After PerformRemoval...");
                //        myRunspace.Open();
                //        complete = PerformRemoveCertificate(config, serverUserName, serverPassword);
                //        myRunspace.Close();
                //        _logger.LogTrace("After Perform Removal...");
                //        break;
                //}

                //_logger.MethodExit();
                //return complete;
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

                throw new Exception(failureMessage);
            }
        }

        private JobResult BindSQLCertificate(string newThumbprint, string renewalThumbprint)
        {
            bool hadError = false;
            var instances = SQLInstanceNames.Split(",");

            foreach (var instanceName in instances)
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Thumbprint", newThumbprint },
                    { "SqlInstanceName", instanceName.Trim() },
                    { "StoreName", _storePath },
                    { "RestartService", RestartSQLService }
                };

                try
                {
                    _results = _psHelper.ExecutePowerShell("Bind-CertificateToSqlInstance", parameters);
                    _logger.LogTrace("Return from executing PS function (Bind-CertificateToSqlInstance)");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error occurred while binding certificate to SQL Instance {instanceName}", ex);
                    hadError= true;
                }
            }

            if (hadError)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = "Unable to bind one or more certificates to the SQL Instances."
                };
            } else 
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = ""
                };
            }
        }

        private JobResult UnBindSQLCertificate()
        {
            bool hadError = false;
            var instances = SQLInstanceNames.Split(",");

            foreach (var instanceName in instances)
            {
                var parameters = new Dictionary<string, object>
                {
                    { "SqlInstanceName", instanceName.Trim() }
                };

                try
                {
                    _results = _psHelper.ExecutePowerShell("UnBind-KFSqlServerCertificate", parameters);
                    _logger.LogTrace("Returned from executing PS function (UnBind-KFSqlServerCertificate)");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error occurred while binding certificate to SQL Instance {instanceName}", ex);
                    hadError = true;
                }
            }

            if (hadError)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = "Unable to unbind one or more certificates from the SQL Instances."
                };
            }
            else
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = _jobHistoryID,
                    FailureMessage = ""
                };
            }
        }
    }
}