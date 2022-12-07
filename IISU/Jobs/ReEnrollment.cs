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
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISU.Jobs
{
    public class ReEnrollment:IReenrollmentJobExtension
    {
        private ILogger _logger;

        private IPAMSecretResolver _resolver;

        public ReEnrollment(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        public string ExtensionName => "IISU";
        
        private string ResolvePamField(string name, string value)
        {
            _logger.LogTrace($"Attempting to resolved PAM eligible field {name}");
            return _resolver.Resolve(value);
        }

        public JobResult ProcessJob(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReEnrollmentUpdate)
        {
            _logger.MethodEntry();
            _logger = LogHandler.GetClassLogger<ReEnrollment>();
            _logger.LogTrace($"Job Configuration: {JsonConvert.SerializeObject(config)}");
            var storePath = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
            _logger.LogTrace($"WinRm Url: {storePath?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{storePath?.WinRmPort}/wsman");

            _logger.LogTrace("Entering ReEnrollment...");
            _logger.LogTrace("Before ReEnrollment...");
            return PerformReEnrollment(config, submitReEnrollmentUpdate);

        }

        private JobResult PerformReEnrollment(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReenrollment)
        {
            try
            {
                _logger.MethodEntry();
                var serverUserName = ResolvePamField("Server UserName", config.ServerUsername);
                var serverPassword = ResolvePamField("Server Password", config.ServerPassword);

                // Extract values necessary to create remote PS connection
                JobProperties properties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                WSManConnectionInfo connectionInfo = new WSManConnectionInfo(new Uri($"{properties?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{properties?.WinRmPort}/wsman"));
                connectionInfo.IncludePortInSPN = properties.SpnPortFlag;
                var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                _logger.LogTrace($"Credentials: UserName:{serverUserName} Password:{serverPassword}");

                connectionInfo.Credential = new PSCredential(serverUserName, pw);
                _logger.LogTrace($"PSCredential Created {pw}");

                // Establish new remote ps session
                _logger.LogTrace("Creating remote PS Workspace");
                using var runSpace = RunspaceFactory.CreateRunspace(connectionInfo);
                _logger.LogTrace("Workspace created");
                runSpace.Open();
                _logger.LogTrace("Workspace opened");

                // NEW
                var ps = PowerShell.Create();
                ps.Runspace = runSpace;

                string CSR = string.Empty;

                var subjectText = config.JobProperties["subjectText"];
                var providerName = config.JobProperties["ProviderName"];
                var keyType = config.JobProperties["keyType"];
                var keySize = config.JobProperties["keySize"];
                var SAN = config.JobProperties["SAN"];

                // If the provider name is null, default it to the Microsoft CA
                if (providerName == null) providerName = "Microsoft Strong Cryptographic Provider";

                // Create the script file
                ps.AddScript("$infFilename = New-TemporaryFile");
                ps.AddScript("$csrFilename = New-TemporaryFile");

                ps.AddScript("if (Test-Path $csrFilename) { Remove-Item $csrFilename }");

                ps.AddScript($"Set-Content $infFilename [NewRequest]");
                ps.AddScript($"Add-Content $infFilename 'Subject = \"{subjectText}\"'");
                ps.AddScript($"Add-Content $infFilename 'ProviderName = \"{providerName}\"'");
                ps.AddScript($"Add-Content $infFilename 'MachineKeySet = True'");
                ps.AddScript($"Add-Content $infFilename 'HashAlgorithm = SHA256'");
                ps.AddScript($"Add-Content $infFilename 'KeyAlgorithm = {keyType}'");
                ps.AddScript($"Add-Content $infFilename 'KeyLength={keySize}'");
                ps.AddScript($"Add-Content $infFilename 'KeySpec = 0'");

                ps.AddScript($"Add-Content $infFilename '[Extensions]'");
                ps.AddScript(@"Add-Content $infFilename '2.5.29.17 = ""{text}""'");

                foreach (string s in SAN.ToString().Split("&"))
                {
                    ps.AddScript($"Add-Content $infFilename '_continue_ = \"{s + "&"}\"'");
                }

                // Execute the -new command
                ps.AddScript($"certreq -new -q $infFilename $csrFilename");
                _logger.LogDebug($"Subject Text: {subjectText}");
                _logger.LogDebug($"SAN: {SAN}");
                _logger.LogDebug($"Provider Name: {providerName}");
                _logger.LogDebug($"Key Type: {keyType}");
                _logger.LogDebug($"Key Size: {keySize}");
                _logger.LogTrace("Attempting to create the CSR by Invoking the script.");

                Collection<PSObject> results = ps.Invoke();
                _logger.LogTrace("Completed the attempt in creating the CSR.");
                ps.Commands.Clear();

                try
                {
                    ps.AddScript($"$CSR = Get-Content $csrFilename");
                    _logger.LogTrace("Attempting to get the contents of the CSR file.");
                    results = ps.Invoke();
                    _logger.LogTrace("Finished getting the CSR Contents.");
                }
                catch (Exception)
                {
                    var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                    throw new PowerShellCertException($"Error creating CSR File. {psError}");
                }
                finally
                {
                    ps.Commands.Clear();

                    // Delete the temp files
                    ps.AddScript("if (Test-Path $infFilename) { Remove-Item -Path $infFilename }");
                    ps.AddScript("if (Test-Path $csrFilename) { Remove-Item -Path $csrFilename }");
                    _logger.LogTrace("Attempt to delete the temporary files.");
                    results = ps.Invoke();
                }

                // Get the byte array
                var CSRContent = ps.Runspace.SessionStateProxy.GetVariable("CSR").ToString();

                // Sign CSR in Keyfactor
                _logger.LogTrace("Get the signed CSR from KF.");
                X509Certificate2 myCert = submitReenrollment.Invoke(CSRContent);

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
                    ps.AddScript("certreq -accept $cerFilename");
                    ps.Invoke();
                    _logger.LogTrace("Successfully bound the certificate to the HSM.");
                    ps.Commands.Clear();

                    // Delete the temp files
                    ps.AddScript("if (Test-Path $infFilename) { Remove-Item -Path $infFilename }");
                    ps.AddScript("if (Test-Path $csrFilename) { Remove-Item -Path $csrFilename }");
                    ps.AddScript("if (Test-Path $cerFilename) { Remove-Item -Path $cerFilename }");
                    _logger.LogTrace("Removing temporary files.");
                    results = ps.Invoke();

                    ps.Commands.Clear();
                    runSpace.Close();

                    // Bind the certificate to IIS
                    var iisManager = new IISManager(config,serverUserName,serverPassword);
                    return iisManager.ReEnrollCertificate(myCert);
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
