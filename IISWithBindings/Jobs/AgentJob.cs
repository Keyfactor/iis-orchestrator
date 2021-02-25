using System;
using System.Linq;
using CSS.Common.Logging;
using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Interfaces;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding2
{
    public abstract class AgentJob : LoggingClientBase, IAgentJobExtension
    {
        public string GetJobClass()
        {
            var attr = GetType().GetCustomAttributes(true).First(a => a.GetType() == typeof(JobAttribute)) as JobAttribute;
            return attr?.JobClass ?? string.Empty;
        }

        public string GetStoreType() => IISWithBinding2Constants.STORE_TYPE_NAME;

        public abstract AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr);

        protected AnyJobCompleteInfo Success(string message = null)
        {

            return new AnyJobCompleteInfo()
            {
                Status = 2,
                Message = message ?? $"{GetJobClass()} Complete"
            };
        }

        protected AnyJobCompleteInfo ThrowError(Exception exception, string jobSection)
        {
            string message = FlattenException(exception);
            Logger.Error($"Error performing {jobSection} in {GetJobClass()} {GetStoreType()} - {message}");
            return new AnyJobCompleteInfo()
            {
                Status = 4,
                Message = message
            };
        }

        private string FlattenException(Exception ex)
        {
            string returnMessage = ex.Message;
            if (ex.InnerException != null)
                returnMessage += (" - " + FlattenException(ex.InnerException));

            return returnMessage;
        }
    }
}