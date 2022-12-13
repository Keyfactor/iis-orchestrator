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

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    public class IISManager 
    {
        private ILogger Logger { get; }
        private string SiteName { get; set; }
        private string IpAddress { get; set; }
        private string HostName { get; set; }
        private long JobHistoryId { get; set; }
        private string Port { get; set; }
        private  string SniFlag { get; set; }
        private string Path { get; set; }
        private string ClientMachine { get; set; }
        private string Protocol { get; set; }
        private string CertContents { get; set; }
        private string PrivateKeyPassword { get; set; }
        private string ServerUserName { get; set; }
        private string ServerPassword { get; set; }
        private JobProperties Properties { get; set; }
        private string RenewalThumbprint { get; set; }
        private WSManConnectionInfo ConnectionInfo { get; set; }


        private X509Certificate2 x509Cert;
        private Runspace runSpace;
        private PowerShell ps;

        #region Constructors
        /// <summary>
        /// Performs a Reenrollment of a certificate in IIS
        /// </summary>
        /// <param name="config"></param>
        public IISManager(ReenrollmentJobConfiguration config,string serverUserName,string serverPassword)
        {
            Logger = LogHandler.GetClassLogger<IISManager>();

            try
            {
                SiteName = config.JobProperties["SiteName"].ToString();
                Port = config.JobProperties["Port"].ToString();
                HostName = config.JobProperties["HostName"]?.ToString();
                Protocol = config.JobProperties["Protocol"].ToString();
                SniFlag = config.JobProperties["SniFlag"].ToString()?.Substring(0, 1);
                IpAddress = config.JobProperties["IPAddress"].ToString();

                PrivateKeyPassword = ""; // A reenrollment does not have a PFX Password
                ServerUserName = serverUserName;
                ServerPassword = serverPassword;
                RenewalThumbprint = ""; // A reenrollment will always be empty
                ClientMachine = config.CertificateStoreDetails.ClientMachine;
                Path = config.CertificateStoreDetails.StorePath;
                CertContents = ""; // Not needed for a reenrollment
                JobHistoryId = config.JobHistoryId;

                Properties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                ConnectionInfo =
                    new WSManConnectionInfo(
                        new Uri($"{Properties?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{Properties?.WinRmPort}/wsman"));
            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating an IIS ReEnrollment Job: {e.Message}", e.InnerException);
            }
        }

        /// <summary>
        /// Performs Management functions of Adding or updating certificates in IIS
        /// </summary>
        /// <param name="config"></param>
        public IISManager(ManagementJobConfiguration config, string serverUserName, string serverPassword)
        {
            Logger = LogHandler.GetClassLogger<IISManager>(); 

            try
            {
                SiteName = config.JobProperties["SiteName"].ToString();
                Port = config.JobProperties["Port"].ToString();
                HostName = config.JobProperties["HostName"]?.ToString();
                Protocol = config.JobProperties["Protocol"].ToString();
                SniFlag = config.JobProperties["SniFlag"].ToString()?.Substring(0, 1);
                IpAddress = config.JobProperties["IPAddress"].ToString();

                PrivateKeyPassword = config.JobCertificate.PrivateKeyPassword;
                ServerUserName = serverUserName;
                ServerPassword = serverPassword;
                ClientMachine = config.CertificateStoreDetails.ClientMachine;
                Path = config.CertificateStoreDetails.StorePath;
                CertContents = config.JobCertificate.Contents;
                JobHistoryId = config.JobHistoryId;

                Properties = JsonConvert.DeserializeObject<JobProperties>(config.CertificateStoreDetails.Properties,
                    new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                ConnectionInfo =
                    new WSManConnectionInfo(
                        new Uri($"{Properties?.WinRmProtocol}://{config.CertificateStoreDetails.ClientMachine}:{Properties?.WinRmPort}/wsman"));

                if (config.JobProperties.ContainsKey("RenewalThumbprint"))
                {
                    RenewalThumbprint = config.JobProperties["RenewalThumbprint"].ToString();
                    Logger.LogTrace($"Found Thumbprint Will Renew all Certs with this thumbprint: {RenewalThumbprint}");
                }

            }
            catch (Exception e)
            {
                throw new Exception($"Error when initiating an IIS Management Job: {e.Message}", e.InnerException);
            }
        }

        #endregion

        public JobResult ReEnrollCertificate(X509Certificate2 certificate)
        {
            x509Cert = certificate;

            try
            {
                // Instanciate a new Powershell instance
                CreatePowerShellInstance();

                return BindCertificate();

            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryId,
                    FailureMessage = $"Error Occurred in ReEnrollCertification {LogHandler.FlattenException(e)}"
                };
            }
        }

       public JobResult AddCertificate()
        {
            try
            {
                Logger.LogTrace($"Creating X509 Cert from: {CertContents}");
                x509Cert = new X509Certificate2(
                    Convert.FromBase64String(CertContents),
                    PrivateKeyPassword,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet |
                    X509KeyStorageFlags.Exportable);
                Logger.LogTrace($"X509 Cert Created With Subject: {x509Cert.SubjectName}");
                Logger.LogTrace(
                    $"Begin Add for Cert Store {$@"\\{ClientMachine}\{Path}"}");

                // Instanciate a new Powershell instance
                CreatePowerShellInstance();

                // Add Certificate 
                var funcScript = @"
                                                    $ErrorActionPreference = ""Stop""

                                                    function InstallPfxToMachineStore([byte[]]$bytes, [string]$password, [string]$storeName) {
                                                        $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, ""LocalMachine""
                                                        $certStore.Open(5)
                                                        $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $bytes, $password, 18 <# Persist, Machine #>
                                                        $certStore.Add($cert)
                                                        $certStore.Close();
                                                    }";

                ps.AddScript(funcScript).AddStatement();
                Logger.LogTrace("InstallPfxToMachineStore Statement Added...");
                ps.AddCommand("InstallPfxToMachineStore")
                    .AddParameter("bytes", Convert.FromBase64String(CertContents))
                    .AddParameter("password", PrivateKeyPassword)
                    .AddParameter("storeName",
                        $@"\\{ClientMachine}\{Path}");
                Logger.LogTrace("InstallPfxToMachineStore Command Added...");

                foreach (var cmd in ps.Commands.Commands)
                {
                    Logger.LogTrace("Logging PowerShell Command");
                    Logger.LogTrace(cmd.CommandText);
                }

                Logger.LogTrace("Invoking ps...");
                ps.Invoke();
                Logger.LogTrace("ps Invoked...");
                if (ps.HadErrors)
                {
                    Logger.LogTrace("ps Has Errors");
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error.ErrorDetails.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryId,
                            FailureMessage =
                                $"Site {Path} on server {ClientMachine}: {psError}"
                        };
                    }
                }

                Logger.LogTrace("Clearing Commands...");
                ps.Commands.Clear();
                Logger.LogTrace("Commands Cleared..");

                // Install the certifiacte
                return BindCertificate();
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryId,
                    FailureMessage = $"Error Occurred in InstallCertificate {LogHandler.FlattenException(e)}"
                };
            }
        }

        private void CreatePowerShellInstance()
        {
            Logger.LogTrace($"IncludePortInSPN: {Properties.SpnPortFlag}");
            ConnectionInfo.IncludePortInSPN = Properties.SpnPortFlag;
            Logger.LogTrace($"Credentials: UserName:{ServerUserName} Password:{ServerPassword}");
            var pw = new NetworkCredential(ServerUserName, ServerPassword)
                .SecurePassword;
            ConnectionInfo.Credential = new PSCredential(ServerUserName, pw);
            Logger.LogTrace($"PSCredential Created {pw}");

            runSpace = RunspaceFactory.CreateRunspace(ConnectionInfo);
            Logger.LogTrace("RunSpace Created");
            runSpace.Open();
            Logger.LogTrace("RunSpace Opened");
            Logger.LogTrace(
                $"Creating Cert Store with ClientMachine: {ClientMachine}, JobProperties: {Path}");
            var _ = new PowerShellCertStore(
                ClientMachine, Path,
                runSpace);
            Logger.LogTrace("Cert Store Created");
            ps = PowerShell.Create();
            Logger.LogTrace("ps created");
            ps.Runspace = runSpace;
            Logger.LogTrace("RunSpace Assigned");
        }

        private JobResult BindCertificate()
        {
            try
            {
                //if thumbprint is there it is a renewal so we have to search all the sites for that thumbprint and renew them all
                if (RenewalThumbprint?.Length > 0)
                {
                    Logger.LogTrace($"Thumbprint Length > 0 {RenewalThumbprint}");
                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    Logger.LogTrace("WebAdministration Imported");
                    var searchScript =
                        "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                    ps.AddScript(searchScript).AddStatement();
                    Logger.LogTrace($"Search Script: {searchScript}");
                    var bindings = ps.Invoke();
                    foreach (var binding in bindings)
                    {
                        Logger.LogTrace("Looping Bindings....");
                        var bindingSiteName = binding.Properties["name"].Value.ToString();
                        var bindingIpAddress = binding.Properties["Bindings"].Value.ToString()?.Split(':')[0];
                        var bindingPort = binding.Properties["Bindings"].Value.ToString()?.Split(':')[1];
                        var bindingHostName = binding.Properties["Bindings"].Value.ToString()?.Split(':')[2];
                        var bindingProtocol = binding.Properties["Protocol"].Value.ToString();
                        var bindingThumbprint = binding.Properties["thumbprint"].Value.ToString();
                        var bindingSniFlg = binding.Properties["sniFlg"].Value.ToString();

                        Logger.LogTrace(
                            $"bindingSiteName: {bindingSiteName}, bindingIpAddress: {bindingIpAddress}, bindingPort: {bindingPort}, bindingHostName: {bindingHostName}, bindingProtocol: {bindingProtocol}, bindingThumbprint: {bindingThumbprint}, bindingSniFlg: {bindingSniFlg}");

                        //if the thumbprint of the renewal request matches the thumbprint of the cert in IIS, then renew it
                        if (RenewalThumbprint == bindingThumbprint)
                        {
                            Logger.LogTrace($"Thumbprint Match {RenewalThumbprint}={bindingThumbprint}");
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
                                Path, //{6}
                                bindingSniFlg); //{7}

                            Logger.LogTrace($"funcScript {funcScript}");
                            ps.AddScript(funcScript);
                            Logger.LogTrace("funcScript added...");
                            ps.Invoke();
                            Logger.LogTrace("funcScript Invoked...");
                            foreach (var cmd in ps.Commands.Commands)
                            {
                                Logger.LogTrace("Logging PowerShell Command");
                                Logger.LogTrace(cmd.CommandText);
                            }

                            ps.Commands.Clear();
                            Logger.LogTrace("Commands Cleared..");
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
                        IpAddress, //{1}
                        Port, //{2}
                        Protocol, //{3}
                        HostName, //{4}
                        x509Cert.Thumbprint, //{5} 
                        Path, //{6}
                        Convert.ToInt16(SniFlag)); //{7}
                    foreach (var cmd in ps.Commands.Commands)
                    {
                        Logger.LogTrace("Logging PowerShell Command");
                        Logger.LogTrace(cmd.CommandText);
                    }

                    Logger.LogTrace($"funcScript {funcScript}");
                    ps.AddScript(funcScript);
                    Logger.LogTrace("funcScript added...");
                    ps.Invoke();
                    Logger.LogTrace("funcScript Invoked...");
                }

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) => current + error.ErrorDetails.Message);
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryId,
                            FailureMessage =
                                $"Site {Path} on server {ClientMachine}: {psError}"
                        };
                    }
                }

                Logger.LogTrace("closing RunSpace...");
                runSpace.Close();
                Logger.LogTrace("RunSpace Closed...");

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryId,
                    FailureMessage = ""
                };
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryId,
                    FailureMessage = $"Error Occurred in InstallCertificate {LogHandler.FlattenException(e)}"
                };
            }
        }
    }
}