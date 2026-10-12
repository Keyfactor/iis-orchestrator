using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models
{
    public class ResultObject
    {
        public const string StatusSuccess = "Success";
        public const string StatusWarning = "Warning";
        public const string StatusSkipped = "Skipped";
        public const string StatusError = "Error";

        public string Status { get; set; }
        public int Code { get; set; }
        public string Step { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// True when Status is Success (case-insensitive).
        /// </summary>
        public bool IsSuccess =>
            string.Equals(Status, StatusSuccess, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Convenience accessor for the Thumbprint value written into Details by
        /// scripts such as Add-KeyfactorCertificate. Returns an empty string when
        /// missing.
        /// </summary>
        public string Thumbprint =>
            Details != null && Details.TryGetValue("Thumbprint", out var v) && v != null
                ? v.ToString()
                : string.Empty;

        /// <summary>
        /// Builds a ResultObject from a PowerShell PSObject that follows the
        /// New-KeyfactorResult contract (Status, Code, Step, Message,
        /// ErrorMessage, Details). Missing properties become sensible defaults.
        /// </summary>
        public static ResultObject FromPSObject(PSObject psObject)
        {
            var result = new ResultObject
            {
                Status = StatusError,
                Code = -1,
                Step = string.Empty,
                Message = string.Empty,
                ErrorMessage = string.Empty,
                Details = new Dictionary<string, object>()
            };

            if (psObject == null)
            {
                result.ErrorMessage = "PowerShell returned a null result object.";
                return result;
            }

            result.Status = psObject.Properties["Status"]?.Value as string ?? StatusError;
            result.Step = psObject.Properties["Step"]?.Value as string ?? string.Empty;
            result.Message = psObject.Properties["Message"]?.Value as string ?? string.Empty;
            result.ErrorMessage = psObject.Properties["ErrorMessage"]?.Value as string ?? string.Empty;

            var codeValue = psObject.Properties["Code"]?.Value;
            if (codeValue is int intCode)
            {
                result.Code = intCode;
            }
            else if (codeValue != null && int.TryParse(codeValue.ToString(), out var parsed))
            {
                result.Code = parsed;
            }

            var detailsValue = psObject.Properties["Details"]?.Value;
            if (detailsValue is PSObject detailsPs && detailsPs.BaseObject is IDictionary dictBase)
            {
                CopyDictionary(dictBase, result.Details);
            }
            else if (detailsValue is IDictionary directDict)
            {
                CopyDictionary(directDict, result.Details);
            }

            return result;
        }

        /// <summary>
        /// Builds a ResultObject from the first item of a PowerShell result
        /// collection. When the collection is null or empty, returns an Error
        /// ResultObject explaining that no result was produced.
        /// </summary>
        public static ResultObject FromPSResults(Collection<PSObject> results)
        {
            if (results == null || results.Count == 0 || results[0] == null)
            {
                return new ResultObject
                {
                    Status = StatusError,
                    Code = -1,
                    Step = "CatchAll",
                    ErrorMessage = "PowerShell script returned no results.",
                    Details = new Dictionary<string, object>()
                };
            }

            return FromPSObject(results[0]);
        }

        private static void CopyDictionary(IDictionary source, Dictionary<string, object> target)
        {
            foreach (DictionaryEntry entry in source)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var value = entry.Value;
                if (value is PSObject psValue)
                {
                    value = psValue.BaseObject ?? psValue;
                }

                target[key] = value;
            }
        }
    }
}
