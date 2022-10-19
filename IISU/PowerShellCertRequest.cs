using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    internal class PowerShellCertRequest : IDisposable
    {
        public Runspace RunSpace { get; set; }
        public string ServerName { get; set; }
        public string StorePath { get; set; }

        private PowerShell ps { get; set; }

        public PowerShellCertRequest(string serverName, string storePath, Runspace runspace)
        {
            ServerName = serverName;
            StorePath = storePath;
            RunSpace = runspace;

            ps = PowerShell.Create();
            ps.Runspace = runspace;
        }

        /// <summary>
        /// Executes the certreq -new command to create the CSR file
        /// </summary>
        /// <returns>Unsigned CSR Filename</returns>
        public string AddNewCertificate(ReenrollmentJobConfiguration config)
        {
            // Define the variables sent from config argument
            // Todo: Set the values that come from the Reenrollment object
            string subject = "CN=Bobs Test Win Cert";
            string providerName = "Microsoft Enhanced RSA and AES Cryptographic Provider";
            string machineKeySet = "true";
            string certificateTemplate = "ExportableWebServer";
            string SAN = "SAN=\"dns=www.bobs.com\"&dns=bobs.com";

            // Create the script file
            ps.AddScript("$infFilename = New-TemporaryFile");
            ps.AddScript("$csrFilename = New-TemporaryFile");

            ps.AddScript("if (Test-Path $csrFilename) { Remove-Item $csrFilename }");

            ps.AddScript($"Set-Content $infFilename [NewRequest]");
            ps.AddScript($"Add-Content $infFilename 'Subject = \"{subject}\"'");
            ps.AddScript($"Add-Content $infFilename 'ProviderName = \"{providerName}\"'");
            ps.AddScript($"Add-Content $infFilename 'MachineKeySet = {machineKeySet}'");

            ps.AddScript($"Add-Content $infFilename [RequestAttributes]");
            ps.AddScript($"Add-Content $infFilename 'CertificateTemplate = {certificateTemplate}'");
            ps.AddScript($"Add-Content $infFilename 'SAN = \"{SAN}\"'");

            // Execute the -new command
            ps.AddScript($"certreq -new -q $infFilename $csrFilename");
            ps.AddScript($"$CSR = Get-Content $csrFilename");

            // Get the returned results back from Powershell
            Collection<PSObject> results = ps.Invoke();

            if (ps.HadErrors)
            {
                var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                throw new PowerShellCertException($"Error creating CSR File. {psError}");
            }

            // Get the byte array
            var CSRArray = ps.Runspace.SessionStateProxy.PSVariable.GetValue("CSR");
            string CSR = string.Empty;
            foreach(object o in (IEnumerable)(CSRArray))
            {
                CSR += o.ToString() + "\n";
            }
            return CSR;
        }

        public void SubmitCertificate()
        {
            // This gets done in KF Commnad
        }

        /// <summary>
        /// Executes the certreq -submit command to bind the signed certificate with the CA
        /// </summary>
        public void AcceptCertificate(string myCertificate)
        {
            ps.AddScript("$cerFilename = New-TemporaryFile");
            ps.Runspace.SessionStateProxy.SetVariable("$certBytes", myCertificate);
            ps.AddScript("$Set-Content $cerFilename $certBytes");
            ps.Invoke();

            ps.AddScript("certreq-accept $cerFilename");
            ps.Invoke();
        }

        public void Dispose()
        {
            try
            {
                ps.AddScript("if (Test-Path $infFilename) { Remove-Item $infFilename }");
                ps.AddScript("if (Test-Path $csrFilename) { Remove-Item $csrFilename }");
                ps.Invoke();
            }
            catch (Exception)
            {
            }
            finally
            {
                ps.Dispose();
            }
        }
    }
}
