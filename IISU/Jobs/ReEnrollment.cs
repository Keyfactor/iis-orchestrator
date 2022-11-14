using System;
using System.Collections;
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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.IISU.Jobs
{
    public class ReEnrollment:IReenrollmentJobExtension
    {
        private readonly ILogger<ReEnrollment> _logger;

        public ReEnrollment(ILogger<ReEnrollment> logger)
        {
            _logger = logger;
        }

        public string ExtensionName => "IISU";
        
        public JobResult ProcessJob(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReEnrollmentUpdate)
        {
            _logger.MethodEntry();
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

                // Extract values necessary to create remote PS connection
                JobProperties properties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                WSManConnectionInfo connectionInfo = new WSManConnectionInfo(new Uri($"{properties?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{properties?.WinRmPort}/wsman"));
                connectionInfo.IncludePortInSPN = properties.SpnPortFlag;
                var pw = new NetworkCredential(config.ServerUsername, config.ServerPassword).SecurePassword;
                _logger.LogTrace($"Credentials: UserName:{config.ServerUsername} Password:{config.ServerPassword}");

                connectionInfo.Credential = new PSCredential(config.ServerUsername, pw);
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

                // Todo:  Parse SAN by '&' and add the below entry for each DSN
                foreach (string s in SAN.ToString().Split("&"))
                {
                    ps.AddScript($"Add-Content $infFilename '_continue_ = \"{s + "&"}\"'");
                }
                
                // Execute the -new command
                ps.AddScript($"certreq -new -q $infFilename $csrFilename");
                
                Collection<PSObject> results = ps.Invoke();
                ps.Commands.Clear();

                try
                {
                    ps.AddScript($"$CSR = Get-Content $csrFilename");
                    results = ps.Invoke();
                }
                catch (Exception e)
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
                    results = ps.Invoke();
                }

                // Get the byte array
                var CSRContent = ps.Runspace.SessionStateProxy.GetVariable("CSR").ToString();
                
                // Sign CSR in Keyfactor
                X509Certificate2 myCert = submitReenrollment.Invoke(CSRContent);

                if (myCert != null)
                {
                    // Get the cert data into string format
                    string csrData = Convert.ToBase64String(myCert.RawData, Base64FormattingOptions.InsertLineBreaks);
                    
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
                    ps.AddScript("certreq -accept $cerFilename");
                    ps.Invoke();
                    ps.Commands.Clear();

                    // Delete the temp files
                    ps.AddScript("if (Test-Path $infFilename) { Remove-Item -Path $infFilename }");
                    ps.AddScript("if (Test-Path $csrFilename) { Remove-Item -Path $csrFilename }");
                    ps.AddScript("if (Test-Path $cerFilename) { Remove-Item -Path $cerFilename }");
                    results = ps.Invoke();

                    ps.Commands.Clear();
                    runSpace.Close();

                    // Bind the certificate to IIS
                    var iisManager = new IISManager(config);
                    return iisManager.ReEnrollCertificate(myCert);
                }
                else
                {
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Failure,
                        JobHistoryId = config.JobHistoryId,
                        FailureMessage = "The ReEnrollment job was unable to sign te CSR.  Please check the formatting of the SAN and other ReEnrollment properties."
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
