﻿// Copyright 2022 Keyfactor
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
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class ClientPSIIManager
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

        /// <summary>
        /// This constructor is used for unit testing
        /// </summary>
        /// <param name="runSpace"></param>
        /// <param name="IPAddress"></param>
        /// <param name="Port"></param>
        /// <param name="HostName"></param>
        /// <param name="Thumbprint"></param>
        /// <param name="StorePath"></param>
        /// <param name="sniFlag"></param>
        public ClientPSIIManager(Runspace runSpace, string SiteName, string Protocol, string IPAddress, string Port, string HostName, string Thumbprint, string StorePath, string sniFlag)
        {
            _logger = LogHandler.GetClassLogger<ClientPSIIManager>();
            _runSpace = runSpace;

            this.SiteName = SiteName;
            this.Protocol = Protocol;
            this.IPAddress = IPAddress;
            this.Port = Port;
            this.HostName = HostName;
            this.RenewalThumbprint = Thumbprint;
            this.StorePath = StorePath;
            this.SniFlag = sniFlag;
        }

        public ClientPSIIManager(ReenrollmentJobConfiguration config, string serverUsername, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<ClientPSIIManager>();

            try
            {
                SiteName = config.JobProperties["SiteName"].ToString();
                Port = config.JobProperties["Port"].ToString();
                HostName = config.JobProperties["HostName"]?.ToString();
                Protocol = config.JobProperties["Protocol"].ToString();
                SniFlag = MigrateSNIFlag(config.JobProperties["SniFlag"]?.ToString());
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
                _runSpace = PsHelper.GetClientPsRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
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
                SniFlag = MigrateSNIFlag(config.JobProperties["SniFlag"]?.ToString());
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
                _runSpace = PsHelper.GetClientPsRunspace(winRmProtocol, ClientMachineName, winRmPort, includePortInSPN, serverUsername, serverPassword);
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

                    // Get the bindings for all the websites
                    ps.AddCommand("Import-Module")
                        .AddParameter("Name", "WebAdministration")
                        .AddStatement();

                    _logger.LogTrace("WebAdministration Imported");
                    var searchScript =
                        "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash;sniFlg=$Bind.sslFlags}}}";
                    ps.AddScript(searchScript).AddStatement();
                    _logger.LogTrace($"Search Script: {searchScript}");
                    var bindings = ps.Invoke();

                    bool hadPSError = false;        // Flag to indicate if any website had problem with binding
                    List<string> bindingSiteErrorMessage = new List<string>();

                    foreach (var binding in bindings)
                    {
                        if (binding.Properties["Protocol"].Value.ToString().Contains("https"))
                        {
                            _logger.LogTrace("Looping Bindings....");
                            var bindingSiteName = binding.Properties["name"].Value.ToString();
                            var bindingBindings = binding.Properties["Bindings"].Value.ToString()?.Split(':');
                            var bindingIpAddress = bindingBindings?.Length > 0 ? bindingBindings[0] : null;
                            var bindingPort = bindingBindings?.Length > 1 ? bindingBindings[1] : null;
                            var bindingHostName = bindingBindings?.Length > 2 ? bindingBindings[2] : null;
                            var bindingProtocol = binding.Properties["Protocol"]?.Value?.ToString();
                            var bindingThumbprint = binding.Properties["thumbprint"]?.Value?.ToString();
                            var bindingSniFlg = binding.Properties["sniFlg"]?.Value?.ToString();

                            _logger.LogTrace(
                                $"bindingSiteName: {bindingSiteName}, bindingIpAddress: {bindingIpAddress}, bindingPort: {bindingPort}, bindingHostName: {bindingHostName}, bindingProtocol: {bindingProtocol}, bindingThumbprint: {bindingThumbprint}, bindingSniFlg: {bindingSniFlg}");

                            //if the thumbprint of the renewal request matches the thumbprint of the cert in IIS, then renew it
                            if (RenewalThumbprint == bindingThumbprint)
                            {
                                _logger.LogTrace($"Thumbprint Match {RenewalThumbprint}={bindingThumbprint}");
                                try
                                {
                                    Collection<PSObject> results = (Collection<PSObject>)PerformIISBinding(bindingSiteName, bindingProtocol, bindingIpAddress, bindingPort, bindingHostName, bindingSniFlg, bindingThumbprint, StorePath);

                                    // Check if PowerShell had any errors for this binding
                                    if (ps.HadErrors)
                                    {
                                        var psError = ps.Streams.Error.ReadAll()
                                            .Aggregate(string.Empty, (current, error) =>
                                                current + (error.ErrorDetails != null && !string.IsNullOrEmpty(error.ErrorDetails.Message)
                                                    ? error.ErrorDetails.Message
                                                    : error.Exception != null
                                                        ? error.Exception.Message
                                                        : error.ToString()) + Environment.NewLine);

                                        string computerName = string.Empty;
                                        if (_runSpace.ConnectionInfo is null)
                                        {
                                            computerName = "localMachine";
                                        }
                                        else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                                        string oops = $"PowerShell Error on Site {bindingSiteName} on {computerName}: {psError}";
                                        bindingSiteErrorMessage.Add(oops);
                                        hadPSError = true;

                                        _logger.LogTrace(oops);
                                    }

                                    // Clear the commands and go to the next website
                                    ps.Commands.Clear();
                                    _logger.LogTrace("Commands Cleared..");
                                }
                                catch (Exception e)
                                {
                                    string computerName = string.Empty;
                                    if (_runSpace.ConnectionInfo is null)
                                    {
                                        computerName = "localMachine";
                                    }
                                    else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                                    string oops = $"Application Exception on Site {bindingSiteName} on {computerName}: {e.Message}";
                                    bindingSiteErrorMessage.Add(oops);
                                    hadPSError = true;

                                    _logger.LogTrace(oops);
                                }
                            }
                        }
                    }

                    if (hadPSError)
                    {
                        // Report errors and job results
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage = string.Join(Environment.NewLine, bindingSiteErrorMessage)
                        };
                    }
                    else
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Success,
                            JobHistoryId = JobHistoryID,
                            FailureMessage = ""
                        };
                    }
                }
                else
                {
                    bool hadError = false;
                    string errorMessage = string.Empty;

                    try
                    {
                        Collection<PSObject> results = (Collection<PSObject>)PerformIISBinding(SiteName, Protocol, IPAddress, Port, HostName, SniFlag, x509Cert.Thumbprint, StorePath);

                        if (ps.HadErrors)
                        {
                            var psError = ps.Streams.Error.ReadAll()
                                .Aggregate(string.Empty, (current, error) =>
                                    current + (error.ErrorDetails != null && !string.IsNullOrEmpty(error.ErrorDetails.Message)
                                        ? error.ErrorDetails.Message
                                        : error.Exception != null
                                            ? error.Exception.Message
                                            : error.ToString()) + Environment.NewLine);

                            errorMessage = psError;
                            hadError = true;

                        }
                    }
                    catch (Exception e)
                    {
                        string computerName = string.Empty;
                        if (_runSpace.ConnectionInfo is null)
                        {
                            computerName = "localMachine";
                        }
                        else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                        errorMessage = $"Binding attempt failed on Site {SiteName} on {computerName}, Application error: {e.Message}";
                        hadError = true;
                        _logger.LogTrace(errorMessage);
                    }

                    if (hadError)
                    {
                        string computerName = string.Empty;
                        if (_runSpace.ConnectionInfo is null)
                        {
                            computerName = "localMachine";
                        }
                        else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage = $"Binding attempt failed on Site {SiteName} on {computerName}: {errorMessage}"
                        };
                    }
                    else
                    {
                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Success,
                            JobHistoryId = JobHistoryID,
                            FailureMessage = ""
                        };
                    }
                }
            }
            catch (Exception e)
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Application Error Occurred in BindCertificate: {LogHandler.FlattenException(e)}"
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
#if NET8_0_OR_GREATER
            bool hadError = false;
            string errorMessage = string.Empty;

            try
            {
                _logger.MethodEntry();

                _runSpace.Open();
                ps = PowerShell.Create();
                ps.Runspace = _runSpace;

                Collection<PSObject> results = (Collection<PSObject>)PerformIISUnBinding(SiteName, Protocol, IPAddress, Port, HostName);

                if (ps.HadErrors)
                {
                    var psError = ps.Streams.Error.ReadAll()
                        .Aggregate(string.Empty, (current, error) =>
                            current + (error.ErrorDetails != null && !string.IsNullOrEmpty(error.ErrorDetails.Message)
                                ? error.ErrorDetails.Message
                                : error.Exception != null
                                    ? error.Exception.Message
                                    : error.ToString()) + Environment.NewLine);

                    errorMessage = psError;
                    hadError = true;

                }
            }
            catch (Exception e)
            {
                string computerName = string.Empty;
                if (_runSpace.ConnectionInfo is null)
                {
                    computerName = "localMachine";
                }
                else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                errorMessage = $"Binding attempt failed on Site {SiteName} on {computerName}, Application error: {e.Message}";
                hadError = true;
                _logger.LogTrace(errorMessage);
            }
            finally
            {
                _runSpace.Close();
                ps.Runspace.Close();
                ps.Dispose();
            }

            if (hadError)
            {
                string computerName = string.Empty;
                if (_runSpace.ConnectionInfo is null)
                {
                    computerName = "localMachine";
                }
                else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Failure,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = $"Binding attempt failed on Site {SiteName} on {computerName}: {errorMessage}"
                };
            }
            else
            {
                return new JobResult
                {
                    Result = OrchestratorJobStatusJobResult.Success,
                    JobHistoryId = JobHistoryID,
                    FailureMessage = ""
                };
            }
#endif
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

                    string computerName = string.Empty;
                    if (_runSpace.ConnectionInfo is null)
                    {
                        computerName = "localMachine";
                    }
                    else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                    return new JobResult
                    {
                        Result = OrchestratorJobStatusJobResult.Failure,
                        JobHistoryId = JobHistoryID,
                        FailureMessage =
                            $"Site {Protocol} binding for Site {SiteName} on {computerName} not found."
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

                        string computerName = string.Empty;
                        if (_runSpace.ConnectionInfo is null)
                        {
                            computerName = "localMachine";
                        }
                        else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                        return new JobResult
                        {
                            Result = OrchestratorJobStatusJobResult.Failure,
                            JobHistoryId = JobHistoryID,
                            FailureMessage =
                                $"Failed to remove {Protocol} binding for Site {SiteName} on {computerName} not found, error {psError}"
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
                string computerName = string.Empty;
                if (_runSpace.ConnectionInfo is null)
                {
                    computerName = "localMachine";
                }
                else { computerName = "Server: " + _runSpace.ConnectionInfo.ComputerName; }

                var failureMessage = $"Unbinding for Site '{StorePath}' on {computerName} with error: {LogHandler.FlattenException(ex)}";
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="webSiteName"></param>
        /// <param name="protocol"></param>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="hostName"></param>
        /// <param name="sslFlags"></param>
        /// <param name="thumbprint"></param>
        /// <param name="storeName"></param>
        /// <returns></returns>
        private object PerformIISUnBinding(string webSiteName, string protocol, string ipAddress, string port, string hostName)
        {
            string funcScript = @"
                param (
                        [string]$SiteName,       # Name of the site
                        [string]$IPAddress,      # IP Address of the binding
                        [string]$Port,           # Port number of the binding
                        [string]$Hostname,       # Hostname (optional)
                        [string]$Protocol = ""https"" # Protocol (default to """"https"""")
                    )

                    # Set Execution Policy (optional, depending on your environment)
                    Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force

                    # Check if the IISAdministration module is already loaded
                    if (-not (Get-Module -Name IISAdministration)) {
                        try {
                            # Attempt to import the IISAdministration module
                            Import-Module IISAdministration -ErrorAction Stop
                        }
                        catch {
                            throw ""Failed to load the IISAdministration module. Ensure it is installed and available.""
                        }
                    }

                    try {
                        # Get the bindings for the specified site
                        $bindings = Get-IISSiteBinding -Name $SiteName
        
                        # Check if any bindings match the specified criteria
                        $matchingBindings = $bindings | Where-Object {
                            ($_.bindingInformation -eq ""${IPAddress}:${Port}:${Hostname}"") -and 
                            ($_.protocol -eq $Protocol)
                        }
        
                        if ($matchingBindings) {
                            # Unbind the matching certificates
                            foreach ($binding in $matchingBindings) {
                                Write-Host """"Removing binding: $($binding.bindingInformation) with protocol: $($binding.protocol)""""
                                Write-Host """"Binding information: 
                                Remove-IISSiteBinding -Name $SiteName -BindingInformation $binding.bindingInformation -Protocol $binding.protocol -confirm:$false
                            }
                            Write-Host """"Successfully removed the matching bindings from the site: $SiteName""""
                        } else {
                            Write-Host """"No matching bindings found for site: $SiteName""""
                        }
                    }
                    catch {
                        throw ""An error occurred while unbinding the certificate from site ${SiteName}: $_""
                    }
            ";

            ps.AddScript(funcScript);
            ps.AddParameter("SiteName", webSiteName);
            ps.AddParameter("IPAddress", ipAddress);
            ps.AddParameter("Port", port);
            ps.AddParameter("Hostname", hostName);
            ps.AddParameter("Protocol", protocol);

            _logger.LogTrace("funcScript added...");
            var results = ps.Invoke();
            _logger.LogTrace("funcScript Invoked...");

            return results;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="webSiteName"></param>
        /// <param name="protocol"></param>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="hostName"></param>
        /// <param name="sslFlags"></param>
        /// <param name="thumbprint"></param>
        /// <param name="storeName"></param>
        /// <returns></returns>
        private object PerformIISBinding(string webSiteName, string protocol, string ipAddress, string port, string hostName, string sslFlags, string thumbprint, string storeName)
        {
            string funcScript = @"
                param (
                    $SiteName,        # The name of the IIS site
                    $IPAddress,       # The IP Address for the binding
                    $Port,            # The port number for the binding
                    $Hostname,        # Hostname for the binding (if any)
                    $Protocol,        # Protocol (e.g., HTTP, HTTPS)
                    $Thumbprint,      # Certificate thumbprint for HTTPS bindings
                    $StoreName,       # Certificate store location (e.g., ""My"" for personal certs)
                    $SslFlags         # SSL flags (if any)
                )

                # Set Execution Policy (optional, depending on your environment)
                Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force

                # Check if the IISAdministration module is available
                #$module = Get-Module -Name IISAdministration -ListAvailable

                #if (-not $module) {
                    #throw ""The IISAdministration module is not installed on this system.""
                #}

                # Check if the IISAdministration module is already loaded
                if (-not (Get-Module -Name IISAdministration)) {
                    try {
                        # Attempt to import the IISAdministration module
                        Import-Module IISAdministration -ErrorAction Stop
                    }
                    catch {
                        throw ""Failed to load the IISAdministration module. Ensure it is installed and available.""
                    }
                }

                # Retrieve the existing binding information
                $myBinding = ""${IPAddress}:${Port}:${Hostname}""
                Write-Host ""myBinding: "" $myBinding

                $siteBindings = Get-IISSiteBinding -Name $SiteName
                $existingBinding = $siteBindings | Where-Object { $_.bindingInformation -eq $myBinding -and $_.protocol -eq $Protocol }
    
                Write-Host ""Binding:"" $existingBinding

                if ($null -ne $existingBinding) {
                    # Remove the existing binding
                    Remove-IISSiteBinding -Name $SiteName -BindingInformation $existingBinding.BindingInformation -Protocol $existingBinding.Protocol -Confirm:$false
        
                    Write-Host ""Removed existing binding: $($existingBinding.BindingInformation)""
                }
        
                # Create the new binding with modified properties
                $newBindingInfo = ""${IPAddress}:${Port}:${Hostname}""
        
                try
                {
                    New-IISSiteBinding -Name $SiteName `
                        -BindingInformation $newBindingInfo `
                        -Protocol $Protocol `
                        -CertificateThumbprint $Thumbprint `
                        -CertStoreLocation $StoreName `
                        -SslFlag $SslFlags

                    Write-Host ""New binding added: $newBindingInfo""
                }
                catch {
                    throw $_
                }
            ";

            ps.AddScript(funcScript);
            ps.AddParameter("SiteName", webSiteName);
            ps.AddParameter("IPAddress", ipAddress);
            ps.AddParameter("Port", port);
            ps.AddParameter("Hostname", hostName);
            ps.AddParameter("Protocol", protocol);
            ps.AddParameter("Thumbprint", thumbprint);
            ps.AddParameter("StoreName", storeName);
            ps.AddParameter("SslFlags", sslFlags);

            _logger.LogTrace("funcScript added...");
            var results = ps.Invoke();
            _logger.LogTrace("funcScript Invoked...");

            return results;
        }

        public static string MigrateSNIFlag(string input)
        {
            // Check if the input is numeric, if so, just return it as an integer
            if (int.TryParse(input, out int numericValue))
            {
                return numericValue.ToString();
            }

            // Handle the string cases
            switch (input.ToLower())
            {
                case "0 - no sni":
                    return "0";
                case "1 - sni enabled":
                    return "1";
                case "2 - non sni binding":
                    return "2";
                case "3 - sni binding":
                    return "3";
                default:
                    throw new ArgumentOutOfRangeException($"Received an invalid value '{input}' for sni/ssl Flag value");
            }
        }
    }
}

