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

// Ignore Spelling: Keyfactor Reenrollment

// 021225 rcp   Cleaned up and removed unnecessary code

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using System.Linq;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql;
using System.Numerics;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPSCertStoreReEnrollment
    {
        private readonly ILogger _logger;
        private readonly IPAMSecretResolver _resolver;

        private PSHelper _psHelper;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private Collection<PSObject>? _results;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

        public ClientPSCertStoreReEnrollment(ILogger logger, IPAMSecretResolver resolver)
        {
            _logger = logger;
            _resolver = resolver;
        }

        public JobResult PerformReEnrollment(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReenrollment, CertStoreBindingTypeENUM bindingType)
        {
            JobResult jobResult = null;

            try
            {
                _logger.MethodEntry();

                var serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                var serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                // Get JobProperties from Config
                var subjectText = config.JobProperties["subjectText"] as string;
                var providerName = config.JobProperties["ProviderName"] as string;
                var keyType = config.JobProperties["keyType"] as string;
                var SAN = config.JobProperties["SAN"] as string;

                int keySize = 0;
                if (config.JobProperties["keySize"] is not null && int.TryParse(config.JobProperties["keySize"].ToString(), out int size))
                {
                    keySize = size;
                }

                // Extract values necessary to create remote PS connection
                JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = config.CertificateStoreDetails.ClientMachine;
                string storePath = config.CertificateStoreDetails.StorePath;

                //_psHelper = new(protocol, port, includePortInSPN, clientMachineName, serverUserName, serverPassword);

                _psHelper = new(protocol, port, includePortInSPN, clientMachineName, serverUserName, serverPassword);
                _psHelper.Initialize();

                using (_psHelper)
                {
                    // First create and return the CSR
                    _logger.LogTrace($"Subject Text: {subjectText}");
                    _logger.LogTrace($"Provider Name: {providerName}");
                    _logger.LogTrace($"Key Type: {keyType}");
                    _logger.LogTrace($"Key Size: {keySize}");
                    _logger.LogTrace($"SAN: {SAN}");

                    string csr = string.Empty;

                    try
                    {
                        _logger.LogTrace("Attempting to Create CSR");
                        csr = CreateCSR(subjectText, providerName, keyType, keySize, SAN);
                        _logger.LogTrace("Returned from creating CSR");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error while attempting to create the CSR: {ex.Message}");
                        throw new Exception($"Unable to create the CSR file.  {ex.Message}");
                    }

                    _logger.LogTrace($"CSR Contents: '{csr}'");

                    if (csr != string.Empty)
                    {
                        // Submit and Sign the CSR in Command
                        _logger.LogTrace("Attempting to sign CSR");
                        X509Certificate2 myCert = submitReenrollment.Invoke(csr);

                        if (myCert == null) { throw new Exception("Command was unable to sign the CSR."); }

                        // Import the certificate
                        string thumbprint = ImportCertificate(myCert.RawData, storePath);

                        // If there is binding, bind it to the correct store type
                        if (thumbprint != null)
                        {
                            switch (bindingType)
                            {
                                case CertStoreBindingTypeENUM.WinIIS:
                                    OrchestratorJobStatusJobResult psResult = OrchestratorJobStatusJobResult.Unknown;
                                    string failureMessage = "";

                                    // Bind Certificate to IIS Site
                                    IISBindingInfo bindingInfo = new IISBindingInfo(config.JobProperties);
                                    var results = WinIISBinding.BindCertificate(_psHelper, bindingInfo, thumbprint, "", storePath);
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

                                    jobResult = new JobResult
                                    {
                                        Result = psResult,
                                        JobHistoryId = config.JobHistoryId,
                                        FailureMessage = failureMessage
                                    };
                                    break;

                                case CertStoreBindingTypeENUM.WinSQL:
                                    // Bind Certificate to SQL Instance
                                    string sqlInstanceNames = "MSSQLSERVER";
                                    if (config.JobProperties.ContainsKey("InstanceName"))
                                    {
                                        sqlInstanceNames = config.JobProperties["InstanceName"]?.ToString() ?? "MSSQLSERVER";
                                    }
                                    WinSqlBinding.BindSQLCertificate(_psHelper, sqlInstanceNames, thumbprint, "", storePath, false);

                                    jobResult = new JobResult
                                    {
                                        Result = OrchestratorJobStatusJobResult.Success,
                                        JobHistoryId = config.JobHistoryId,
                                        FailureMessage = ""
                                    };

                                    break;
                            }
                        }
                        else
                        {
                            jobResult = new JobResult
                            {
                                Result = OrchestratorJobStatusJobResult.Failure,
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage = "There was no thumbprint to bind."
                            };
                        }
                    }
                    else
                    {
                        jobResult = new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = config.JobHistoryId,
                            FailureMessage = "No CSR was generated to perform a reenrollment.  Please check the logs for further details."
                        };

                    }
                }

                return jobResult;

            }
            catch (Exception ex)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = ex.Message
                };
            }
            finally
            {
                if (_psHelper != null)
                {
                    _psHelper.Terminate();
                }
            }
        }

        private string CreateCSR(string subjectText, string providerName, string keyType, int keySize, string SAN)
        {
            string errorMsg = "";

            try
            {
                string myCSR = "";

                _logger.LogTrace("Entering ReEnrollment function: CreateCSR");

                // Set the parameters for the function
                var parameters = new Dictionary<string, object>
                {
                    { "subjectText", subjectText },
                    { "providerName", providerName },
                    { "keyType", keyType },
                    { "keyLength", keySize },
                    { "SAN", SAN }
                };
                _logger.LogInformation("Attempting to execute PS function (New-CsrEnrollment)");
                _results = _psHelper.ExecutePowerShell("New-CsrEnrollment", parameters);
                _logger.LogInformation("Returned from executing PS function (New-CsrEnrollment)");

                // This should return the CSR that was generated
                if (_results == null || _results.Count == 0)
                {
                    _logger.LogError("No results were returned, resulting in no CSR created.");
                }
                else if (_results.Count == 1)
                {
                    myCSR = _results[0]?.ToString();
                    if (!string.IsNullOrEmpty(myCSR))
                    {
                        _logger.LogTrace("Created a CSR.");
                    }
                    else
                    {
                        _logger.LogError("The returned result is empty, resulting in no CSR created.");
                    }
                }
                else // _results.Count > 1
                {
                    var messages = string.Join(Environment.NewLine, _results.Select(r => r?.ToString()));
                    errorMsg = "Multiple results returned, indicating potential errors and no CSR was created.\n";
                    errorMsg += $"Details:{Environment.NewLine}{messages}";
                    _logger.LogError(errorMsg);

                    throw new ApplicationException(errorMsg);

                }

                return myCSR;
            }
            catch (ApplicationException appEx)
            {
                throw new Exception(appEx.Message);
            }
            catch (Exception ex)
            {
                var failureMessage = $"ReEnrollment error at Creating CSR with error: '{ex.Message}'";
                _logger.LogError(LogHandler.FlattenException(ex));

                throw new Exception(failureMessage);
            }
        }

        private string ImportCertificate(byte[] certificateRawData, string storeName)
        {
            try
            {
                string myThumbprint = "";

                _logger.LogTrace("Entering ReEnrollment function: ImportCertificate");

                // Set the parameters for the function
                var parameters = new Dictionary<string, object>
                {
                    { "rawData", certificateRawData },
                    { "storeName", storeName }
                };

                _logger.LogTrace("Attempting to execute PS function (Import-SignedCertificate)");
                _results = _psHelper.ExecutePowerShell("Import-SignedCertificate", parameters);
                _logger.LogTrace("Returned from executing PS function (Import-SignedCertificate)");

                // This should return the CSR that was generated
                if (_results != null && _results.Count > 0)
                {
                    myThumbprint = _results[0].ToString();
                    _logger.LogTrace($"Imported the CSR and returned the following thumbprint: {myThumbprint}");
                }
                else
                {
                    _logger.LogError("No results were returned, resulting in no CSR created.");
                }

                return myThumbprint;

            }
            catch (Exception ex)
            {
                var failureMessage = $"ReEnrollment error while attempting to import the certificate with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogError(failureMessage);

                throw new Exception(failureMessage);
            }
        }

    }
}
