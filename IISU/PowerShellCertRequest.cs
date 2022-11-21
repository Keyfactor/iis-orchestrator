using Keyfactor.Orchestrators.Extensions;
using Newtonsoft.Json;
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

        public PowerShellCertRequest(WSManConnectionInfo connectionInfo, string serverName, string storePath) //, Runspace runspace)
        {
            ServerName = serverName;
            StorePath = storePath;
            //RunSpace = runspace;

            ps = PowerShell.Create();
            ps.Runspace = RunspaceFactory.CreateRunspace(connectionInfo); // = runspace;
            ps.Runspace.Open();
        }

        /// <summary>
        /// Executes the certreq -new command to create the CSR file
        /// </summary>
        /// <returns>Unsigned CSR Filename</returns>
        public string AddNewCertificate(ReenrollmentJobConfiguration config)
        {
            string CSR = string.Empty;

            var subjectText = config.JobProperties["subjectText"];
            var providerName = config.JobProperties["ProviderName"];
            //var keyType = config.JobProperties["keyType"];
            //var keySize = config.JobProperties["keySize"];
            var SAN = config.JobProperties["SAN"];

            try
            {
                // Create the script file
                ps.AddScript("$infFilename = New-TemporaryFile");
                ps.AddScript("$csrFilename = New-TemporaryFile");

                ps.AddScript("if (Test-Path $csrFilename) { Remove-Item $csrFilename }");

                //Collection<PSObject> results = ps.Invoke();

                ps.AddScript($"Set-Content $infFilename [NewRequest]");
                ps.AddScript($"Add-Content $infFilename 'Subject = \"{subjectText}\"'");
                ps.AddScript($"Add-Content $infFilename 'ProviderName = \"{providerName}\"'");
                ps.AddScript($"Add-Content $infFilename 'MachineKeySet = True");
                ps.AddScript($"Add-Content $infFilename 'KeySpec = 0");

                //results = ps.Invoke();

                ps.AddScript($"Add-Content $infFilename [RequestAttributes]");
                ps.AddScript($"Add-Content $infFilename 'SAN = \"{SAN}\"'");

                //results = ps.Invoke();

                // Execute the -new command
                ps.AddScript($"certreq -new -q $infFilename $csrFilename");
                Collection<PSObject> results = ps.Invoke();

                ps.AddScript($"$CSR = Get-Content $csrFilename");

                // Get the returned results back from Powershell
                // Collection<PSObject> results = ps.Invoke();
                results = ps.Invoke();

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                    throw new PowerShellCertException($"Error creating CSR File. {psError}");
                }

                // Get the byte array
                var CSRArray = ps.Runspace.SessionStateProxy.PSVariable.GetValue("CSR");
                
                foreach (object o in (IEnumerable)(CSRArray))
                {
                    CSR += o.ToString() + "\n";
                }

            return CSR;

            }
            catch (Exception ex)
            {
                throw ex;
            }
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
