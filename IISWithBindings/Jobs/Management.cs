
using Keyfactor.Platform.Extensions.Agents;
using Keyfactor.Platform.Extensions.Agents.Delegates;
using Keyfactor.Platform.Extensions.Agents.Enums;
using Keyfactor.Platform.Extensions.Agents.Interfaces;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    [Job(JobTypes.MANAGEMENT)]
    public class Management: AgentJob, IAgentJobExtension
    {
        public override AnyJobCompleteInfo processJob(AnyJobConfigInfo config, SubmitInventoryUpdate submitInventory, SubmitEnrollmentRequest submitEnrollmentRequest, SubmitDiscoveryResults sdr)
        {

            AnyJobCompleteInfo complete = new AnyJobCompleteInfo()
            {
                Status = 4,
                Message = "Invalid Management Operation"
            };

            switch (config.Job.OperationType)
            {
                case AnyJobOperationType.Add:
                    complete = PerformAddition(config);
                    break;
                case AnyJobOperationType.Remove:
                    complete = PerformRemoval(config);
                    break;
            }
            return complete;
        }

        private AnyJobCompleteInfo PerformRemoval(AnyJobConfigInfo config)
        {
            try
            {
                StorePath storePath = JsonConvert.DeserializeObject<StorePath>(config.Store.Properties.ToString(), new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                Logger.Trace($"Begin Removal for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));
                connInfo.IncludePortInSPN = storePath.SPNPortFlag;
                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;

                        ps.AddCommand("Import-Module")
                            .AddParameter("Name", "WebAdministration")
                            .AddStatement();

                        ps.AddCommand("Get-WebBinding")
                            .AddParameter("Protocol", storePath.Protocol)
                            .AddParameter("Name", storePath.SiteName)
                            .AddParameter("Port", storePath.Port)
                            .AddParameter("HostHeader", storePath.HostName)
                            .AddStatement();
                        //ps.AddScript($"(Get-WebBinding -Protocol {storePath.Protocol} -Name {storePath.SiteName} -Port {storePath.Port} -HostHeader {storePath.HostName})").AddStatement();

                        var foundBindings = ps.Invoke();
                        if (foundBindings.Count == 0){
                            return new AnyJobCompleteInfo() { Status = 4, Message = $"{storePath.Protocol} binding for Site {storePath.SiteName} on server {config.Store.ClientMachine} not found." };
                        }

                        ps.Commands.Clear();
                        ps.AddCommand("Import-Module")
                            .AddParameter("Name", "WebAdministration")
                            .AddStatement();
                        foreach (var binding in foundBindings)
                        {
                            ps.AddCommand($"Remove-WebBinding")
                                .AddParameter("Name", storePath.SiteName)
                                .AddParameter("BindingInformation",$"{binding.Properties["bindingInformation"]?.Value}")
                                .AddStatement();
                            var result = ps.Invoke();
                            if (ps.HadErrors)
                            {
                                return new AnyJobCompleteInfo() { Status = 4, Message = $"Failed to remove {storePath.Protocol} binding for Site {storePath.SiteName} on server {config.Store.ClientMachine}." };
                            }
                        }
                        psCertStore.RemoveCertificate(config.Job.Alias);
                        runspace.Close();
                    }
                }
                return new AnyJobCompleteInfo { Status = 2, Message = "" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }

        private AnyJobCompleteInfo PerformAddition(AnyJobConfigInfo config)
        {
            try
            {
                StorePath storePath = JsonConvert.DeserializeObject<StorePath>(config.Store.Properties.ToString(), new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Populate });

                WSManConnectionInfo connInfo = new WSManConnectionInfo(new Uri($"http://{config.Store.ClientMachine}:5985/wsman"));
                connInfo.IncludePortInSPN = storePath.SPNPortFlag;
                SecureString pw = new NetworkCredential(config.Server.Username, config.Server.Password).SecurePassword;
                connInfo.Credential = new PSCredential(config.Server.Username, pw);

                X509Certificate2 x509Cert = new X509Certificate2(Convert.FromBase64String(config.Job.EntryContents), config.Job.PfxPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                Logger.Trace($"Begin Add for Cert Store {$@"\\{config.Store.ClientMachine}\{config.Store.StorePath}"}");

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connInfo))
                {   
                    runspace.Open();
                    PowerShellCertStore psCertStore = new PowerShellCertStore(config.Store.ClientMachine, config.Store.StorePath, runspace);
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;

                        string funcScript = @"
                                                    $ErrorActionPreference = ""Stop""

                                                    function InstallPfxToMachineStore([byte[]]$bytes, [string]$password, [string]$storeName) {
                                                        $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, ""LocalMachine""
                                                        $certStore.Open(5)
                                                        $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $bytes, $password, 18 <# Persist, Machine #>
                                                        $certStore.Add($cert)
                                                        $certStore.Close();
                                                    }";

                        ps.AddScript(funcScript).AddStatement();
                        ps.AddCommand("InstallPfxToMachineStore")
                            .AddParameter("bytes", Convert.FromBase64String(config.Job.EntryContents))
                            .AddParameter("password", config.Job.PfxPassword)
                            .AddParameter("storeName", $@"\\{config.Store.ClientMachine}\{config.Store.StorePath}");

                        ps.Invoke();

                        if (ps.HadErrors)
                        {
                            return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}" };
                        }

                        ps.Commands.Clear();
                        funcScript = string.Format(@"
                                            $ErrorActionPreference = ""Stop""

                                            $IISInstalled = Get-Module -ListAvailable | where {{$_.Name -eq ""WebAdministration""}}
                                            if($IISInstalled) {{
                                                Import-Module WebAdministration
                                                Get-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -Port ""{2}"" -Protocol ""{3}"" |
                                                    ForEach-Object {{ Remove-WebBinding -BindingInformation  $_.bindingInformation }}

                                                New-WebBinding -Name ""{0}"" -IPAddress ""{1}"" -HostHeader ""{4}"" -Port ""{2}"" -Protocol ""{3}"" -SslFlags ""{7}""
                                                Get-WebBinding -Name ""{0}"" -Port ""{2}"" -Protocol ""{3}"" | 
                                                    ForEach-Object {{ $_.AddSslCertificate(""{5}"", ""{6}"") }}
                                            }}", storePath.SiteName,        //{0} 
                                                 storePath.IP,              //{1}
                                                 storePath.Port,            //{2}
                                                 storePath.Protocol,        //{3}
                                                 storePath.HostName,        //{4}
                                                 x509Cert.Thumbprint,       //{5} 
                                                 config.Store.StorePath,    //{6}
                                                 (int)storePath.SniFlag);   //{7}

                        ps.AddScript(funcScript);
                        ps.Invoke();

                        if (ps.HadErrors)
                        {
                            return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ps.Streams.Error.ReadAll().First().ErrorDetails.Message}" };
                        }
                    
                        runspace.Close();
                    }
                }
                    
                return new AnyJobCompleteInfo() { Status = 2, Message = "Addition of certificate and binding complete" };
            }
            catch (Exception ex)
            {
                Logger.Trace(ex);
                return new AnyJobCompleteInfo() { Status = 4, Message = $"Site {config.Store.StorePath} on server {config.Store.ClientMachine}: {ex.Message}" };
            }
        }
    }
}
