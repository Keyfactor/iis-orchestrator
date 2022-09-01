using System;
using System.Linq;
using System.Management.Automation;
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




            throw new NotImplementedException();

        }
    }
}
