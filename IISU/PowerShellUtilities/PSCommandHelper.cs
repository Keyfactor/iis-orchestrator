using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities
{
    public class PSCommandHelper
    {
     /// <summary>
     /// 
     /// </summary>
     /// <param name="ps"></param>
     /// <param name="certStorePath"></param>
     /// <returns>List<X509Certificate2></returns>
     /// <exception cref="CertificateStoreException"></exception>
        public static List<X509Certificate2> GetChildItem(PowerShell ps, string certStorePath)
        {
            string output = string.Empty;
            string errorMsg = string.Empty;
            List<X509Certificate2> certificates = new List<X509Certificate2>();

            var script = $"Get-ChildItem Cert:{certStorePath}";
            ps.AddScript(script);

            // Establish a Powershell output object
            PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();
            ps.Streams.Error.DataAdded += (object sender, DataAddedEventArgs e) =>
            {
                errorMsg = ((PSDataCollection<ErrorRecord>)sender)[e.Index].ToString();
            };

            IAsyncResult result = ps.BeginInvoke<PSObject, PSObject>(null, outputCollection);
            ps.EndInvoke(result);

            foreach (var outputItem in outputCollection)
            {
                X509Certificate2 cert = (X509Certificate2)outputItem.BaseObject;
                certificates.Add(cert);
            }

            ps.Commands.Clear();

            if (!string.IsNullOrEmpty(errorMsg))
                throw new CertificateStoreException(errorMsg);

            return certificates;
        }
    }
}
