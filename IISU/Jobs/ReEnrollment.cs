using System;
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
            return PerformReEnrollment(config);

        }

        private JobResult PerformReEnrollment(ReenrollmentJobConfiguration config)
        {
            try
            {
                _logger.MethodEntry();

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
