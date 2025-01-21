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

using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class PSHelper : IDisposable
    {
        private static ILogger _logger;

        private PowerShell PS;
        private Collection<PSObject> _PSSession = new Collection<PSObject>();

        private string scriptFileLocation = string.Empty;
        private string tempKeyFilePath;

        private string protocol;
        private string port;
        private bool useSPN;
        private string machineName;
        private string? argument;

        private string serverUserName;
        private string serverPassword;

        private bool isLocalMachine;
        public bool IsLocalMachine
        {
            get { return isLocalMachine; }
            private set { isLocalMachine = value; }
        }

        private string clientMachineName;
        public string ClientMachineName
        {
            get { return clientMachineName; }
            private set
            {
                clientMachineName = value;

                // Break the clientMachineName into parts
                string[] parts = clientMachineName.Split('|');

                // Extract the client machine name and arguments based upon the number of parts
                machineName = parts.Length > 1 ? parts[0] : clientMachineName;
                argument = parts.Length > 1 ? parts[1] : null;

                // Determine if this is truly a local connection
                isLocalMachine = (machineName != null && (machineName.ToLower() == "localhost" || machineName.ToLower() == "localmachine")) ||
                                 (argument != null && argument.ToLower() == "localmachine");
                clientMachineName = isLocalMachine ? argument ?? machineName : machineName;
            }
        }

        public PSHelper(string protocol, string port, bool useSPN, string clientMachineName, string serverUserName, string serverPassword)
        {
            this.protocol = protocol.ToLower();
            this.port = port;
            this.useSPN = useSPN;
            ClientMachineName = clientMachineName;
            this.serverUserName = serverUserName;
            this.serverPassword = serverPassword;

            _logger = LogHandler.GetClassLogger<PSHelper>();
            _logger.LogTrace("Entered PSHelper Constructor");
            _logger.LogTrace($"Protocol: {this.protocol}");
            _logger.LogTrace($"Port: {this.port}");
            _logger.LogTrace($"UseSPN: {this.useSPN}");
            _logger.LogTrace($"ClientMachineName: {ClientMachineName}");
            _logger.LogTrace("Constructor Completed");
        }

        public void Initialize()
        {
            _logger.LogTrace("Entered PSHelper.Initialize()");

            PS = PowerShell.Create();

            // Add listeners to raise events
            PS.Streams.Debug.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Error.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Information.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Verbose.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Warning.DataAdded += PSHelper.ProcessPowerShellScriptEvent;

            _logger.LogDebug($"isLocalMachine flag set to: {isLocalMachine}");
            _logger.LogDebug($"Protocol is set to: {protocol}");

            scriptFileLocation = FindPSLocation(AppDomain.CurrentDomain.BaseDirectory, "WinCertFull.ps1");
            if (scriptFileLocation == null) { throw new Exception("Unable to find the accompanying PowerShell Script file: WinCertFull.ps1"); }

            _logger.LogTrace($"Script file located here: {scriptFileLocation}");

            if (!isLocalMachine)
            {
                InitializeRemoteSession();
            }
            else
            {
                InitializeLocalSession();
            }
        }

        private void InitializeRemoteSession()
        {
            if (protocol == "ssh")
            {
                _logger.LogTrace("Initializing SSH connection");

                try
                {
                    _logger.LogInformation("Attempting to create a temporary key file");
                    tempKeyFilePath = createPrivateKeyFile();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error while creating temporary KeyFilePath: {ex.Message}");
                    throw new Exception("Error while creating temporary KeyFilePath.");
                }


                Hashtable options = new Hashtable
                {
                    { "StrictHostKeyChecking", "No" },
                    { "UserKnownHostsFile", "/dev/null" }
                };

                PS.AddCommand("New-PSSession")
                    .AddParameter("Options", options)
                    .AddParameter("HostName", ClientMachineName)
                    .AddParameter("UserName", serverUserName);

                // TODO: THIS IS FOR TESTING ONLY <REMOVE THIS AFTER TESTING>
                if (serverPassword != null)
                {
                    // TODO:  Need to write out to file and pass file name.  For right now, the password is the filename.
                    _logger.LogTrace($"Current KeyFilePath: {tempKeyFilePath}");
                    PS.AddParameter("KeyFilePath", tempKeyFilePath);
                }

            }
            else
            {
                _logger.LogTrace("Initializing WinRM connection");
                var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                PSCredential myCreds = new PSCredential(serverUserName, pw);

                // Create the PSSessionOption object
                var sessionOption = new PSSessionOption
                {
                    IncludePortInSPN = useSPN
                };

                PS.AddCommand("New-PSSession")
                .AddParameter("ComputerName", ClientMachineName)
                .AddParameter("Port", port)
                .AddParameter("Credential", myCreds)
                .AddParameter("SessionOption", sessionOption);
            }

            _logger.LogTrace("Attempting to invoke PS-Session command on remote machine.");
            _PSSession = PS.Invoke();

            if (_PSSession.Count > 0)
            {
                _logger.LogTrace("Session Invoked...Checking for errors.");
                PS.Commands.Clear();
                _logger.LogTrace("PS-Session established");

                PS.AddCommand("Invoke-Command")
                    .AddParameter("Session", _PSSession)
                    .AddParameter("ScriptBlock", ScriptBlock.Create(PSHelper.LoadScript(scriptFileLocation)));

                var results = PS.Invoke();
                CheckErrors();
            }
            else
            {
                throw new Exception("Failed to create the remote PowerShell Session.");
            }

        }

        private void InitializeLocalSession()
        {
            _logger.LogTrace("Setting Execution Policy to Unrestricted");
            PS.AddScript("Set-ExecutionPolicy Unrestricted -Scope Process -Force");
            PS.Invoke();  // Ensure the script is invoked and loaded
            CheckErrors();

            PS.Commands.Clear();  // Clear commands after loading functions

            // Trying this to get IISAdministration loaded!!
            PowerShellProcessInstance psInstance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
            Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), psInstance);
            rs.Open();

            PS.Runspace = rs;

            _logger.LogTrace("Setting script file into memory");
            PS.AddScript(". '" + scriptFileLocation + "'");
            PS.Invoke();  // Ensure the script is invoked and loaded
            CheckErrors();

            PS.Commands.Clear();  // Clear commands after loading functions
        }


        public void Terminate()
        {
            PS.Commands.Clear();
            if (PS != null)
            {
                PS.AddCommand("Remove-PSSession").AddParameter("Session", _PSSession);
                PS.Invoke();
                CheckErrors();
            }

            if (File.Exists(tempKeyFilePath))
            {
                try
                {
                    File.Delete(tempKeyFilePath);
                    _logger.LogTrace($"Temporary KeyFilePath deleted: {tempKeyFilePath}");
                }
                catch (Exception)
                {
                    _logger.LogError($"Error while deleting KeyFilePath.");
                }
            }

            try
            {
                PS.Runspace.Close();
            }
            catch (Exception)
            {
            }

            PS.Dispose();
        }

        public Collection<PSObject>? ExecuteFunction(string functionName)
        {
            return ExecutePowerShell(functionName);
        }

        public Collection<PSObject>? ExecutePowerShell(string commandName, Dictionary<string, object>? parameters = null)
        {
            using (PowerShell PS = PowerShell.Create())
            {
                try
                {
                    string scriptBlock;

                    if (parameters != null && parameters.Count > 0)
                    {
                        _logger.LogTrace("Creating script block with parameters.");
                        string paramBlock = string.Join(", ", parameters.Select(p => $"[{p.Value.GetType().Name}] ${p.Key}"));
                        string paramUsage = string.Join(" ", parameters.Select(p => $"-{p.Key} ${p.Key}"));

                        scriptBlock = $@"
                    param({paramBlock})
                    {commandName} {paramUsage}
                ";
                    }
                    else
                    {
                        _logger.LogTrace("Creating script block with no parameters.");
                        scriptBlock = commandName;
                    }

                    PS.AddCommand("Invoke-Command")
                        .AddParameter("ScriptBlock", ScriptBlock.Create(scriptBlock));

                    if (!isLocalMachine)
                    {
                        PS.AddParameter("Session", _PSSession);
                    }

                    if (parameters != null && parameters.Count > 0)
                    {
                        PS.AddParameter("ArgumentList", parameters.Values.ToArray());
                    }

                    _logger.LogTrace($"Executing script block:\n{scriptBlock}");

                    var results = PS.Invoke();

                    if (PS.HadErrors)
                    {
                        string errorMessages = string.Join("; ", PS.Streams.Error.Select(e => e.ToString()));
                        _logger.LogError($"{errorMessages}");
                        throw new Exception($"PowerShell execution errors: {errorMessages}");
                    }

                    return results;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error while executing script: {ex.Message}");
                    throw new Exception($"An error occurred while attempting to execute the PowerShell script: {ex.Message}.");
                }
                finally
                {
                    PS.Commands.Clear();
                }
            }
        }


        public Collection<PSObject>? ExecutePowerShellV2(string commandName, Dictionary<string, object>? parameters = null)
        {
            try
            {
                if (!isLocalMachine)
                {
                    string scriptBlock;

                    if (parameters != null && parameters.Count > 0)
                    {
                        _logger.LogTrace("Creating script block with parameters.");
                        string paramBlock = string.Join(", ", parameters.Keys.Select(key => $"[{parameters[key].GetType().Name}] ${key}"));
                        string paramUsage = string.Join(" ", parameters.Keys.Select(key => $"-{key} ${key}"));

                        scriptBlock = $@"
                            param({paramBlock})
                            {commandName} {paramUsage}
                        ";
                        }
                    else
                    {
                        _logger.LogTrace("Creating script block with no parameters.");
                        scriptBlock = $@"
                            {commandName}
                        ";
                    }


                    PS.AddCommand("Invoke-Command")
                        .AddParameter("Session", _PSSession) // send session only when necessary (remote)
                        .AddParameter("ScriptBlock", ScriptBlock.Create(commandName));

                    if (parameters != null && parameters.Count > 0)
                    {
                        PS.AddParameter("ArgumentList", parameters.Values.ToArray());
                    }
                }
                else
                {
                    PS.AddCommand(commandName);

                    if (parameters != null)
                    {
                        foreach (var parameter in parameters)
                        {
                            PS.AddParameter(parameter.Key, parameter.Value);
                        }
                    }
                }

                bool hadErrors = false;
                string errorList = string.Empty;
                _logger.LogTrace($"Script block:\n{commandName}");

                var results = PS.Invoke();
                CheckErrors();

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while executing script: {ex.Message}");
                throw new Exception(ex.Message);
            }
            finally
            {
                PS.Commands.Clear();
            }
        }

        [Obsolete]
        public Collection<PSObject>? ExecuteCommand(string scriptBlock, Dictionary<string, object> parameters = null)
        {
            _logger.LogTrace("Executing PowerShell Script");

            using (PS)
            {
                PS.AddCommand("Invoke-Command")
                  .AddParameter("Session", _PSSession) // send session only when necessary (remote)
                  .AddParameter("ScriptBlock", ScriptBlock.Create(scriptBlock));

                // Add parameters to the script block
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        PS.AddParameter("ArgumentList", parameters.Values.ToArray());
                    }
                }

                try
                {
                    _logger.LogTrace("Ready to invoke the script");
                    var results = PS.Invoke();
                    CheckErrors();

                    var jsonResults = results[0].ToString();
                    var certInfoList = JsonSerializer.Deserialize<List<CertificateInfo>>(jsonResults);

                    return results;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception: {ex.Message}");
                    return null;
                }

            }
        }

        private void CheckErrors()
        {
            _logger.LogTrace("Checking PowerShell session for errors.");

            string errorList = string.Empty;
            if (PS.HadErrors)
            {
                errorList = string.Empty;
                foreach (var error in PS.Streams.Error)
                {
                    errorList += error + "\n";
                    _logger.LogError($"Error: {error}");
                }

                throw new Exception(errorList);
            }
        }

        public static string LoadScript(string scriptFileName)
        {
            string scriptFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerShellScripts", scriptFileName);
            _logger.LogTrace($"Attempting to load script {scriptFilePath}");

            if (File.Exists(scriptFilePath))
            {
                return File.ReadAllText(scriptFilePath);
            }else
            { throw new Exception($"File: {scriptFilePath} was not found."); }
        }

        private static string FindPSLocation(string directory, string fileName)
        {
            try
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFullPath(file);
                    }
                }

                foreach (string subDir in Directory.GetDirectories(directory))
                {
                    string result = FindPSLocation(subDir, fileName);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        public static void ProcessPowerShellScriptEvent(object? sender, DataAddedEventArgs e)
        {
            if (sender != null)
            {
                {
                    switch (sender)
                    {
                        case PSDataCollection<DebugRecord>:
                            var debugMessages = sender as PSDataCollection<DebugRecord>;
                            if (debugMessages != null)
                            {
                                var debugMessage = debugMessages[e.Index];
                                _logger.LogDebug($"Debug: {debugMessage.Message}");
                            }
                            break;
                        case PSDataCollection<VerboseRecord>:
                            var verboseMessages = sender as PSDataCollection<VerboseRecord>;
                            if (verboseMessages != null)
                            {
                                var verboseMessage = verboseMessages[e.Index];
                                _logger.LogTrace($"Verbose: {verboseMessage.Message}");
                            }
                            break;
                        case PSDataCollection<ErrorRecord>:
                            var errorMessages = sender as PSDataCollection<ErrorRecord>;
                            if (errorMessages != null)
                            {
                                var errorMessage = errorMessages[e.Index];
                                _logger.LogError($"Error: {errorMessage.Exception.Message}");
                            }
                            break;
                        case PSDataCollection<InformationRecord>:
                            var infoMessages = sender as PSDataCollection<InformationRecord>;
                            if (infoMessages != null)
                            {
                                var infoMessage = infoMessages[e.Index];
                                _logger.LogInformation($"INFO: {infoMessage.MessageData.ToString()}");
                            }
                            break;

                        case PSDataCollection<WarningRecord>:
                            var warningMessages = sender as PSDataCollection<WarningRecord>;
                            if (warningMessages != null)
                            {
                                var warningMessage = warningMessages[e.Index];
                                _logger.LogWarning($"WARN: {warningMessage.Message}");
                            }
                            break;
                        default:
                            break;

                    }
                }
            }
        }

        public void Dispose()
        {
            if (PS != null)
            {
                // Remove the listeners
                PS.Streams.Debug.DataAdded -= PSHelper.ProcessPowerShellScriptEvent;
                PS.Streams.Error.DataAdded -= PSHelper.ProcessPowerShellScriptEvent;
                PS.Streams.Information.DataAdded -= PSHelper.ProcessPowerShellScriptEvent;
                PS.Streams.Verbose.DataAdded -= PSHelper.ProcessPowerShellScriptEvent;
                PS.Streams.Warning.DataAdded -= PSHelper.ProcessPowerShellScriptEvent;

                PS.Dispose();
            }
        }


        // Code below is ORIGINAL
        public static Runspace GetClientPsRunspace(string winRmProtocol, string clientMachineName, string winRmPort, bool includePortInSpn, string serverUserName, string serverPassword)
        {
            _logger = LogHandler.GetClassLogger<PSHelper>();
            _logger.MethodEntry();

            // 2.4 - Client Machine Name now follows the naming conventions of {clientMachineName}|{localMachine}
            // If the clientMachineName is just 'localhost', it will maintain that as locally only (as previously)
            // If there is no 2nd part to the clientMachineName, a remote PowerShell session will be created

            // Break the clientMachineName into parts
            string[] parts = clientMachineName.Split('|');

            // Extract the client machine name and arguments based upon the number of parts
            string machineName = parts.Length > 1 ? parts[0] : clientMachineName;
            string argument = parts.Length > 1 ? parts[1] : null;

            // Determine if this is truly a local connection
            bool isLocal = (machineName.ToLower() == "localhost") || (argument != null && argument.ToLower() == "localmachine");

            _logger.LogInformation($"Full clientMachineName={clientMachineName} | machineName={machineName} | argument={argument} | isLocal={isLocal}");

            if (isLocal)
            {
#if NET6_0
                PowerShellProcessInstance instance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
                Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), instance);
                return rs;
#elif NET8_0_OR_GREATER
                try 
	            {	        
                    InitialSessionState iss = InitialSessionState.CreateDefault();
                    Runspace rs = RunspaceFactory.CreateRunspace(iss);
                    return rs;
	            }
	            catch (global::System.Exception)
	            {
                    throw new Exception($"An error occurred while attempting to create the PowerShell instance.  This version requires .Net8 and PowerShell SDK 7.2 or greater.  Please verify the version of .Net8 and PowerShell installed on your machine.");
	            }
#endif
            }
            else
            {
                var connInfo = new WSManConnectionInfo(new Uri($"{winRmProtocol}://{clientMachineName}:{winRmPort}/wsman"));
                connInfo.IncludePortInSPN = includePortInSpn;

                _logger.LogTrace($"Creating remote session at: {connInfo.ConnectionUri}");

                if (!string.IsNullOrEmpty(serverUserName))
                {
                    _logger.LogTrace($"Credentials Specified");
                    var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                    connInfo.Credential = new PSCredential(serverUserName, pw);
                }
                return RunspaceFactory.CreateRunspace(connInfo);
            }
        }

        public static IEnumerable<string> GetCSPList(Runspace myRunspace)
        {
            _logger.LogTrace("Getting the list of Crypto Service Providers");

            using var ps = PowerShell.Create();

            ps.Runspace = myRunspace;

            var certStoreScript = $@"
                                $certUtilOutput = certutil -csplist

                                $cspInfoList = @()
                                foreach ($line in $certUtilOutput) {{
                                    if ($line -match ""Provider Name:"") {{
                                        $cspName = ($line -split "":"")[1].Trim()
                                        $cspInfoList += $cspName
                                    }}
                                }}

                                $cspInfoList";

            ps.AddScript(certStoreScript);

            foreach (var result in ps.Invoke())
            {
                var cspName = result?.BaseObject?.ToString();
                if (cspName != null) { yield return cspName; }
            }

            _logger.LogInformation("No Crypto Service Providers were found");
            yield return null;
        }

        public static bool IsCSPFound(IEnumerable<string> cspList, string userCSP)
        {
            foreach (var csp in cspList)
            {
                if (string.Equals(csp, userCSP, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogTrace($"CSP found: {csp}");
                    return true;
                }
            }
            _logger.LogTrace($"CSP: {userCSP} was not found");
            return false;
        }

        private string createPrivateKeyFile()
        {
            string tmpFile = Path.GetTempFileName();  // "logs/AdminFile";
            _logger.LogTrace($"Created temporary KeyFilePath: {tmpFile}, writing bytes.");

            File.WriteAllText(tmpFile, formatPrivateKey(serverPassword));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogTrace($"Changing permissions on Windows temp file: {tmpFile}.");
                File.SetAttributes(tmpFile, FileAttributes.ReadOnly);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ProcessStartInfo chmodInfo = new ProcessStartInfo()
                {
                    FileName = "/bin/chmod",
                    Arguments = "600 " + tmpFile,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process chmodProcess = new Process() { StartInfo = chmodInfo })
                {
                    chmodProcess.Start();
                    chmodProcess.WaitForExit();
                    if (chmodProcess.ExitCode == 0)
                    {
                        _logger.LogInformation("File permissions set to 600.");
                    }
                    else
                    {
                        _logger.LogWarning("Failed to set file permissions.");
                    }
                }
            }

            return tmpFile;
        }

        private static string formatPrivateKey(string privateKey)
        {
            String keyType = privateKey.Contains("OPENSSH PRIVATE KEY") ? "OPENSSH" : "RSA";

            return privateKey.Replace($" {keyType} PRIVATE ", "^^^").Replace(" ", System.Environment.NewLine).Replace("^^^", $" {keyType} PRIVATE ") + System.Environment.NewLine;
        }

        //private string formatPrivateKey(string privateKey)
        //{
        //    // Identify the markers in the private key
        //    string beginMarker = "-----BEGIN OPENSSH PRIVATE KEY-----";
        //    string endMarker = "-----END OPENSSH PRIVATE KEY-----";

        //    // Locate the positions of the markers
        //    int beginIndex = privateKey.IndexOf(beginMarker);
        //    int endIndex = privateKey.IndexOf(endMarker);

        //    // Split the string into three parts: before, key content, and after
        //    string beforeKey = privateKey.Substring(0, beginIndex + beginMarker.Length);
        //    string keyContent = privateKey.Substring(beginIndex + beginMarker.Length, endIndex - beginIndex - beginMarker.Length);
        //    string afterKey = privateKey.Substring(endIndex);

        //    // Replace spaces with actual carriage return and line feed in key content
        //    keyContent = keyContent.Replace(" ", "\r\n");

        //    // Construct the final string with the correctly formatted key
        //    string replacedFile = beforeKey + keyContent + afterKey + "\r\n";

        //    // Log the modified string
        //    _logger.LogTrace(replacedFile);

        //    return replacedFile;
        //}

    }
}
