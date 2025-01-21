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

// Ignore Spelling: Keyfactor

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using System.Linq;
using System.IO;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPSCertStoreReEnrollment
    {
        private readonly ILogger _logger;
        private readonly IPAMSecretResolver _resolver;

        private PSHelper _psHelper;
        private Collection<PSObject>? _results;

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
                        throw new Exception("Unable to create the CSR file.  Check the Orchestrator Logs for more information");
                    }

                    _logger.LogTrace($"CSR Contents: '{csr}'");

                    if (csr != string.Empty)
                    {
                        // Submit and Sign the CSR in Command
                        _logger.LogTrace("Attempting to sign CSR");
                        X509Certificate2 myCert = submitReenrollment.Invoke(csr.ToString());

                        // Import the certificate
                        string thumbprint = ImportCertificate(myCert.RawData, storePath);

                        // If there is binding, bind it to the correct store type
                        if (thumbprint != null)
                        {
                            switch (bindingType)
                            {
                                case CertStoreBindingTypeENUM.WinIIS:
                                    // Bind Certificate to IIS Site
                                    IISBindingInfo bindingInfo = new IISBindingInfo(config.JobProperties);
                                    WinIISBinding.BindCertificate(_psHelper, bindingInfo, thumbprint, "", storePath);
                                    break;
                                case CertStoreBindingTypeENUM.WinSQL:
                                    // Bind Certificate to SQL Instance
                                    string sqlInstanceNames = "MSSQLSERVER";
                                    if (config.JobProperties.ContainsKey("InstanceName"))
                                    {
                                        sqlInstanceNames = config.JobProperties["InstanceName"]?.ToString() ?? "MSSQLSERVER";
                                    }
                                    WinSqlBinding.BindSQLCertificate(_psHelper, sqlInstanceNames, thumbprint, "", storePath, false);
                                    break;
                            }

                        }



                        jobResult = new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Success,
                            JobHistoryId = config.JobHistoryId,
                            FailureMessage = ""
                        };

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

        public JobResult PerformReEnrollmentORIG(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReenrollment, CertStoreBindingTypeENUM bindingType)
        {
            bool hasError = false;

            try
            {
                _logger.MethodEntry();
                var serverUserName = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server UserName", config.ServerUsername);
                var serverPassword = PAMUtilities.ResolvePAMField(_resolver, _logger, "Server Password", config.ServerPassword);

                // Extract values necessary to create remote PS connection
                JobProperties jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                string protocol = jobProperties.WinRmProtocol;
                string port = jobProperties.WinRmPort;
                bool IncludePortInSPN = jobProperties.SpnPortFlag;
                string clientMachineName = config.CertificateStoreDetails.ClientMachine;
                string storePath = config.CertificateStoreDetails.StorePath;

                _logger.LogTrace($"Establishing runspace on client machine: {clientMachineName}");
                using var runSpace = PSHelper.GetClientPsRunspace(protocol, clientMachineName, port, IncludePortInSPN, serverUserName, serverPassword);
                
                _logger.LogTrace("Runspace created");
                runSpace.Open();
                _logger.LogTrace("Runspace opened");

                PowerShell ps = PowerShell.Create();
                ps.Runspace = runSpace;

                string CSR = string.Empty;

                var subjectText = config.JobProperties["subjectText"];
                var providerName = config.JobProperties["ProviderName"];
                var keyType = config.JobProperties["keyType"];
                var keySize = config.JobProperties["keySize"];
                var SAN = config.JobProperties["SAN"];

                Collection<PSObject> results;

                // If the provider name is null, default it to the Microsoft CA
                providerName ??= "Microsoft Strong Cryptographic Provider";

                // Create the script file
                ps.AddScript("$infFilename = New-TemporaryFile");
                ps.AddScript("$csrFilename = New-TemporaryFile");

                ps.AddScript("if (Test-Path $csrFilename) { Remove-Item $csrFilename }");

                ps.AddScript($"Set-Content $infFilename -Value [NewRequest]");
                ps.AddScript($"Add-Content $infFilename -Value 'Subject = \"{subjectText}\"'");
                ps.AddScript($"Add-Content $infFilename -Value 'ProviderName = \"{providerName}\"'");
                ps.AddScript($"Add-Content $infFilename -Value 'MachineKeySet = True'");
                ps.AddScript($"Add-Content $infFilename -Value 'HashAlgorithm = SHA256'");
                ps.AddScript($"Add-Content $infFilename -Value 'KeyAlgorithm = {keyType}'");
                ps.AddScript($"Add-Content $infFilename -Value 'KeyLength={keySize}'");
                ps.AddScript($"Add-Content $infFilename -Value 'KeySpec = 0'");

                if (SAN != null)
                {
                    ps.AddScript($"Add-Content $infFilename -Value '[Extensions]'");
                    ps.AddScript(@"Add-Content $infFilename -Value '2.5.29.17 = ""{text}""'");

                    foreach (string s in SAN.ToString().Split("&"))
                    {
                        ps.AddScript($"Add-Content $infFilename -Value '_continue_ = \"{s + "&"}\"'");
                    }
                }

                try
                {
                    // Get INF file for debugging
                    ps.AddScript("$name = $infFilename.FullName");
                    ps.AddScript("$name");
                    results = ps.Invoke();

                    string fname = results[0].ToString();
                    string infContent = File.ReadAllText(fname);

                    _logger.LogDebug($"Contents of {fname}:");
                    _logger.LogDebug(infContent);
                }
                catch (Exception)
                {
                }

                // Execute the -new command
                ps.AddScript($"certreq -new -q $infFilename $csrFilename");
                _logger.LogDebug($"Subject Text: {subjectText}");
                _logger.LogDebug($"SAN: {SAN}");
                _logger.LogDebug($"Provider Name: {providerName}");
                _logger.LogDebug($"Key Type: {keyType}");
                _logger.LogDebug($"Key Size: {keySize}");
                _logger.LogTrace("Attempting to create the CSR by Invoking the script.");

                results = ps.Invoke();
                _logger.LogTrace("Completed the attempt in creating the CSR.");

                ps.Commands.Clear();

                try
                {
                    ps.AddScript($"$CSR = Get-Content $csrFilename -Raw");
                    _logger.LogTrace("Attempting to get the contents of the CSR file.");
                    results = ps.Invoke();
                    _logger.LogTrace("Finished getting the CSR Contents.");
                }
                catch (Exception)
                {
                    var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);

                    hasError = true;

                    throw new CertificateStoreException($"Error creating CSR File. {psError}");
                }
                finally
                {
                    ps.Commands.Clear();

                    // Delete the temp files
                    ps.AddScript("if (Test-Path $infFilename) { Remove-Item -Path $infFilename }");
                    ps.AddScript("if (Test-Path $csrFilename) { Remove-Item -Path $csrFilename }");
                    _logger.LogTrace("Attempt to delete the temporary files.");
                    results = ps.Invoke();

                    if (hasError) runSpace.Close();
                }

                // Get the byte array
                var RawContent = runSpace.SessionStateProxy.GetVariable("CSR");

                // Sign CSR in Keyfactor
                _logger.LogTrace("Get the signed CSR from KF.");
                X509Certificate2 myCert = submitReenrollment.Invoke(RawContent.ToString());

                if (myCert != null)
                {
                    // Get the cert data into string format
                    string csrData = Convert.ToBase64String(myCert.RawData, Base64FormattingOptions.InsertLineBreaks);

                    _logger.LogTrace("Creating the text version of the certificate.");

                    // Write out the cert file
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("-----BEGIN CERTIFICATE-----");
                    sb.AppendLine(csrData);
                    sb.AppendLine("-----END CERTIFICATE-----");

                    ps.AddScript("$cerFilename = New-TemporaryFile");
                    ps.AddScript($"Set-Content $cerFilename '{sb}'");

                    results = ps.Invoke();
                    ps.Commands.Clear();

                    // Accept the signed cert
                    _logger.LogTrace("Attempting to accept or bind the certificate to the HSM.");

                    ps.AddScript($"Set-Location -Path Cert:\\localmachine\\'{config.CertificateStoreDetails.StorePath}'");
                    ps.AddScript($"Import-Certificate -Filepath $cerFilename");
                    ps.Invoke();
                    _logger.LogTrace("Successfully bound the certificate.");

                    ps.Commands.Clear();

                    // Delete the temp files
                    ps.AddScript("if (Test-Path $infFilename) { Remove-Item -Path $infFilename }");
                    ps.AddScript("if (Test-Path $csrFilename) { Remove-Item -Path $csrFilename }");
                    ps.AddScript("if (Test-Path $cerFilename) { Remove-Item -Path $cerFilename }");
                    _logger.LogTrace("Removing temporary files.");
                    results = ps.Invoke();

                    ps.Commands.Clear();
                    runSpace.Close();

                    // Default results
                    JobResult result = new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Success,
                        JobHistoryId = config.JobHistoryId,
                        FailureMessage = ""
                    };

                    // Do specific bindings
                    switch (bindingType)
                    {
                        case CertStoreBindingTypeENUM.WinIIS:
                            // Bind the certificate to IIS
                            ClientPSIIManager iisManager = new ClientPSIIManager(config, serverUserName, serverPassword);
                            result = iisManager.BindCertificate(myCert);
                            // Provide logging information
                            if (result.Result == OrchestratorJobStatusJobResult.Success) { _logger.LogInformation("Certificate was successfully bound to the IIS Server."); }
                            else { _logger.LogInformation("There was an issue while attempting to bind the certificate to the IIS Server.  Check the logs for more information."); }
                            break;

                        case CertStoreBindingTypeENUM.WinSQL:

                            // Bind to SQL Server
                            ClientPsSqlManager sqlManager = new ClientPsSqlManager(config, serverUserName, serverPassword);
                            result = sqlManager.BindCertificates("", myCert);

                            // Provide logging information
                            if (result.Result == OrchestratorJobStatusJobResult.Success) { _logger.LogInformation("Certificate was successfully bound to the SQL Server."); }
                            else { _logger.LogInformation("There was an issue while attempting to bind the certificate to the SQL Server.  Check the logs for more information."); }
                            break;

                    }

                    ps.Commands.Clear();
                    runSpace.Close();

                    return result;
                }
                else
                {
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Failure,
                        JobHistoryId = config.JobHistoryId,
                        FailureMessage = "The ReEnrollment job was unable to sign the CSR.  Please check the formatting of the SAN and other ReEnrollment properties."
                    };
                }

            }
            catch (PSRemotingTransportException psEx)
            {
                var failureMessage = $"ReEnrollment job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with a PowerShell Transport Exception: {psEx.Message}";
                _logger.LogError(failureMessage + LogHandler.FlattenException(psEx));

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };

            }
            catch (Exception ex)
            {
                var failureMessage = $"ReEnrollment job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = config.JobHistoryId,
                    FailureMessage = failureMessage
                };
            }
        }
    }
}
