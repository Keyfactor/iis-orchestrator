using System;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
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

                connectionInfo.Credential = new System.Management.Automation.PSCredential(config.ServerUsername, pw);
                _logger.LogTrace($"PSCredential Created {pw}");

                // Establish new remote ps session
                _logger.LogTrace("Creating remote PS Workspace");
                using var runSpace = RunspaceFactory.CreateRunspace(connectionInfo);
                _logger.LogTrace("Workspace created");
                runSpace.Open();
                _logger.LogTrace("Workspace opened");

                using var _ = new PowerShellCertRequest(config.CertificateStoreDetails.ClientMachine, config.CertificateStoreDetails.StorePath, runSpace);

                // Build INF file and create CSR
                string CSRFilename = _.AddNewCertificate(config);

                // Sign CSR in Keyfactor
                X509Certificate2 myCert = submitReenrollment.Invoke(CSRFilename);

                // Accept the signed cert
                //x509object as encoded string to send back to powershell
                //X509Certificate2 myCert = X509Certificate2.CreateFromCertFile("Myfile");
                _.AcceptCertificate(myCert.GetRawCertDataString());
                
                runSpace.Close();

                // Bind the certificate to IIS
                var iisManager = new IISManager();
                return iisManager.ReEnrollCertificate(config);
                
            }
            catch (Exception ex)
            {
                var failureMessage = $"Add job failed for Site '{config.CertificateStoreDetails.StorePath}' on server '{config.CertificateStoreDetails.ClientMachine}' with error: '{LogHandler.FlattenException(ex)}'";
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
