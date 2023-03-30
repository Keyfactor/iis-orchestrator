// Copyright 2022 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ClientPSIIManager
    {
        private string SiteName { get; set; }
        private string Port { get; set; }
        private string Protocol { get; set; }
        private string HostName { get; set; }
        private string SniFlag { get; set; }
        private string IPAddress { get; set; }

        private string RenewalThumbprint { get; set; } = "";

        private string CertContents { get; set; } = "";

        private string PrivateKeyPassword { get; set; } = "";

        private string ClientMachineName { get; set; }
        private string StorePath { get; set; }

        private long JobHistoryID { get; set; }

        private readonly ILogger _logger;
        private readonly Runspace _runSpace;

        private PowerShell ps;

        //public ClientPSIIManager(ILogger logger, Runspace runSpace)
        //{
        //    _logger = logger;
        //    _runSpace = runSpace;

        //    ps = PowerShell.Create();
        //    ps.Runspace = _runSpace;
        //}

        //public ClientPSIIManager(ILogger logger, Runspace runSpace, PowerShell powerShell)
        //{
        //    _logger = logger;
        //    _runSpace = runSpace;

        //    ps = powerShell;
        //}

        public ClientPSIIManager(ReenrollmentJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<ClientPSIIManager>();

            try
            {
                SiteName = config.JobProperties["SiteName"].ToString();
                Port = config.JobProperties["Port"].ToString();
                HostName = config.JobProperties["HostName"]?.ToString();
                Protocol = config.JobProperties["Protocol"].ToString();
                SniFlag = config.JobProperties["SniFlag"]?.ToString()[..1];
                IPAddress = config.JobProperties["IPAddress"].ToString();

                PrivateKeyPassword = ""; // A reenrollment does not have a PFX Password
                RenewalThumbprint = ""; // A reenrollment will always be empty
                CertContents = ""; // Not needed for a reenrollment

                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                StorePath = config.CertificateStoreDetails.StorePath;

                JobHistoryID = config.JobHistoryId;

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = PSHelper.GetClientPSRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating an IIS ReEnrollment Job: {e.Message}", e.InnerException);
            }
        }

        public ClientPSIIManager(ManagementJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<ClientPSIIManager>();

            try
            {
                SiteName = config.JobProperties["SiteName"].ToString();
                Port = config.JobProperties["Port"].ToString();
                HostName = config.JobProperties["HostName"]?.ToString();
                Protocol = config.JobProperties["Protocol"].ToString();
                SniFlag = config.JobProperties["SniFlag"].ToString()?[..1];
                IPAddress = config.JobProperties["IPAddress"].ToString();

                PrivateKeyPassword = ""; // A reenrollment does not have a PFX Password
                RenewalThumbprint = ""; // A reenrollment will always be empty
                CertContents = ""; // Not needed for a reenrollment

                ClientMachineName = config.CertificateStoreDetails.ClientMachine;
                StorePath = config.CertificateStoreDetails.StorePath;

                JobHistoryID = config.JobHistoryId;

                if (config.JobProperties.ContainsKey("RenewalThumbprint"))
                {
                    RenewalThumbprint = config.JobProperties["RenewalThumbprint"].ToString();
                    _logger.LogTrace($"Found Thumbprint Will Renew all Certs with this thumbprint: {RenewalThumbprint}");
                }

                // Establish PowerShell Runspace
                var jobProperties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });
                string winRmProtocol = jobProperties.WinRmProtocol;
                string winRmPort = jobProperties.WinRmPort;
                bool includePortInSPN = jobProperties.SpnPortFlag;

                _logger.LogTrace($"Establishing runspace on client machine: {ClientMachineName}");
                _runSpace = PSHelper.GetClientPSRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating an IIS ReEnrollment Job: {e.Message}", e.InnerException);
            }
        }

        public JobResult BindCertificate(X509Certificate2 x509Cert)
        {
            try
            {
                _logger.MethodEntry();

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;

                //if thumbprint is there it is a renewal so we have to search all the sites for that thumbprint and renew them all
                if (RenewalThumbprint?.Length > 0)
                {
                    _logger.LogTrace($"Thumbprint Length > 0 {RenewalThumbprint}");
                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    _logger.LogTrace("WebAdministration Imported");
                    var searchScript =
                        "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                    ps.AddScript(searchScript).AddStatement();
                    _logger.LogTrace($"Search Script: {searchScript}");
                    var bindings = ps.Invoke();
                    foreach (var binding in bindings)
                    {
                        if (binding.Properties["Protocol"].Value.ToString().Contains("http"))
                        {
                            _logger.LogTrace("Looping Bindings....");
                            var bindingSiteName = binding.Properties["name"].Value.ToString();
                            var bindingIpAddress = binding.Properties["Bindings"].Value.ToString()?.Split(':')[0];
                            var bindingPort = binding.Properties["Bindings"].Value.ToString()?.Split(':')[1];
                            var bindingHostName = binding.Properties["Bindings"].Value.ToString()?.Split(':')[2];
                            var bindingProtocol = binding.Properties["Protocol"].Value.ToString();
                            var bindingThumbprint = binding.Properties["thumbprint"].Value.ToString();
                            var bindingSniFlg = binding.Properties["sniFlg"].Value.ToString();

                            _logger.LogTrace(
                                $"bindingSiteName: {bindingSiteName}, bindingIpAddress: {bindingIpAddress}, bindingPort: {bindingPort}, bindingHostName: {bindingHostName}, bindingProtocol: {bindingProtocol}, bindingThumbprint: {bindingThumbprint}, bindingSniFlg: {bindingSniFlg}");

                            //if the thumbprint of the renewal request matches the thumbprint of the cert in IIS, then renew it
                            if (RenewalThumbprint == bindingThumbprint)
                            {
                                _logger.LogTrace($"Thumbprint Match {RenewalThumbprint}={bindingThumbprint}");
                                var funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", bindingSiteName, //{0} 
                                    bindingIpAddress, //{1}
                                    bindingPort, //{2}
                                    bindingProtocol, //{3}
                                    bindingHostName, //{4}
                                    x509Cert.Thumbprint, //{5} 
                                    StorePath, //{6}
                                    bindingSniFlg); //{7}

                                _logger.LogTrace($"funcScript {funcScript}");
                                ps.AddScript(funcScript);
                                _logger.LogTrace("funcScript added...");
                                ps.Invoke();
                                _logger.LogTrace("funcScript Invoked...");
                                foreach (var cmd in ps.Commands.Commands)
                                {
                                    _logger.LogTrace("Logging PowerShell Command");
                                    _logger.LogTrace(cmd.CommandText);
                                }

                                ps.Commands.Clear();
                                _logger.LogTrace("Commands Cleared..");
                            }
                        }
                    }
                }
                else
                {
                    var funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -Port ""{2}"" -Protocol ""{3}"" -HostHeader ""{4}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", SiteName, //{0} 
                        IPAddress, //{1}
                        Port, //{2}
                        Protocol, //{3}
                        HostName, //{4}
                        x509Cert.Thumbprint, //{5} 
                        StorePath, //{6}
                        Convert.ToInt16(SniFlag)); //{7}
                    foreach (var cmd in ps.Commands.Commands)
                    {
                        _logger.LogTrace("Logging PowerShell Command");
                        _logger.LogTrace(cmd.CommandText);
                    }

                    _logger.LogTrace($"funcScript {funcScript}");
                    ps.AddScript(funcScript);
                    _logger.LogTrace("funcScript added...");
                    ps.Invoke();
                    _logger.LogTrace("funcScript Invoked...");
                }

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error.ErrorDetails.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage =
                                $"Site {StorePath} on server {_runSpace.ConnectionInfo.ComputerName}: {psError}"
                        };
                    }
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Error Occurred in InstallCertificate {LogHandler.FlattenException(e)}"
                };
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }
        }

        public JobResult UnBindCertificate()
        {
            try
            {
                _logger.MethodEntry();

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;

                ps.AddCommand("Import-Module")
                    .AddParameter("Name", "WebAdministration")
                    .AddStatement();

                _logger.LogTrace("WebAdministration Imported");

                ps.AddCommand("Get-WebBinding")
                    .AddParameter("Protocol", Protocol)
                    .AddParameter("Name", SiteName)
                    .AddParameter("Port", Port)
                    .AddParameter("HostHeader", HostName)
                    .AddParameter("IPAddress", IPAddress)
                    .AddStatement();

                _logger.LogTrace("Get-WebBinding Set");
                var foundBindings = ps.Invoke();
                _logger.LogTrace("foundBindings Invoked");

                if (foundBindings.Count == 0)
                {
                    _logger.LogTrace($"{foundBindings.Count} Bindings Found...");
                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Failure,
                        JobHistoryId = JobHistoryID,
                        FailureMessage =
                            $"Site {Protocol} binding for Site {SiteName} on server {_runSpace.ConnectionInfo.ComputerName} not found."
                    };
                }

                //Log Commands out for debugging purposes
                foreach (var cmd in ps.Commands.Commands)
                {
                    _logger.LogTrace("Logging PowerShell Command");
                    _logger.LogTrace(cmd.CommandText);
                }

                ps.Commands.Clear();
                _logger.LogTrace("Cleared Commands");

                ps.AddCommand("Import-Module")
                    .AddParameter("Name", "WebAdministration")
                    .AddStatement();

                _logger.LogTrace("Imported WebAdministration Module");

                foreach (var binding in foundBindings)
                {
                    ps.AddCommand("Remove-WebBinding")
                        .AddParameter("Name", SiteName)
                        .AddParameter("BindingInformation",
                            $"{binding.Properties["bindingInformation"]?.Value}")
                        .AddStatement();

                    //Log Commands out for debugging purposes
                    foreach (var cmd in ps.Commands.Commands)
                    {
                        _logger.LogTrace("Logging PowerShell Command");
                        _logger.LogTrace(cmd.CommandText);
                    }

                    var _ = ps.Invoke();
                    _logger.LogTrace("Invoked Remove-WebBinding");

                    if (ps.HadErrors)
                    {
                        _logger.LogTrace("PowerShell Had Errors");
                        var psError = ps.Streams.Error.ReadAll().Aggregate(String.Empty, (current, error) => current + error.ErrorDetails.Message);
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage =
                                $"Failed to remove {Protocol} binding for Site {SiteName} on server {_runSpace.ConnectionInfo.ComputerName} not found, error {psError}"
                        };
                    }
                }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = ""
                };

            }
            catch (Exception ex)
            {
                var failureMessage = $"Unbinging for Site '{StorePath}' on server '{_runSpace.ConnectionInfo.ComputerName}' with error: '{LogHandler.FlattenException(ex)}'";
                _logger.LogWarning(failureMessage);

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = failureMessage
                };
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }
        }
    }
}

