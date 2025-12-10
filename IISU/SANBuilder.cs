using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class SANBuilder
    {
        public Dictionary<string, string[]> SANs { get; set; } = new Dictionary<string, string[]>();
        public SANBuilder(Dictionary<string, string[]> sans)
        {
            SANs = sans ?? throw new ArgumentNullException(nameof(sans));
        }

        public string BuildSanString()
        {
            if (SANs == null || SANs.Count == 0)
                return string.Empty;

            var parts = new List<string>();

            foreach (var entry in SANs)
            {
                string key = NormalizeSanKey(entry.Key);
                if (entry.Value == null) continue;

                parts.AddRange(
                    entry.Value
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => $"{key}={v.Trim()}")
                );
            }

            return string.Join("&", parts);
        }

        /// <summary>
        /// Normalize SAN type keys to RFC-compliant names.
        /// </summary>
        private static string NormalizeSanKey(string key)
        {
            return key.Trim().ToLower() switch
            {
                "dns" => "dns",
                "ip" or "ip4" or "ip6" => "ipaddress",
                "email" or "rfc822" => "email",
                "uri" => "uri",
                "upn" => "upn",
                _ => key.ToLower() // fallback
            };
        }

        public override string ToString()
        {
            if (SANs == null || SANs.Count == 0)
                return "No SANs defined.";

            var lines = new List<string>();
            foreach (var entry in SANs)
            {
                string key = NormalizeSanKey(entry.Key);
                string joined = entry.Value != null && entry.Value.Length > 0
                    ? string.Join(", ", entry.Value)
                    : "(none)";
                lines.Add($"{key.ToUpper()}: {joined}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
