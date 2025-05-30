// Copyright 2025 Keyfactor
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

// 021225 rcp   2.6.0   Cleaned up and verified code

using Keyfactor.Logging;
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
using System.Security.AccessControl;
using System.Threading;

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
            _logger.LogTrace($"PowerShell SDK Location: {typeof(System.Management.Automation.PowerShell).Assembly.Location}");
            _logger.LogTrace($".NET Runtime Version: {RuntimeInformation.FrameworkDescription}");

            PS = PowerShell.Create();

            // Add listeners to raise events
            PS.Streams.Debug.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Error.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Information.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Verbose.DataAdded += PSHelper.ProcessPowerShellScriptEvent;
            PS.Streams.Warning.DataAdded += PSHelper.ProcessPowerShellScriptEvent;

            _logger.LogDebug($"isLocalMachine flag set to: {isLocalMachine}");
            _logger.LogDebug($"Protocol is set to: {protocol}");

            scriptFileLocation = FindPSLocation(AppDomain.CurrentDomain.BaseDirectory, "WinCertScripts.ps1");
            if (scriptFileLocation == null) { throw new Exception("Unable to find the accompanying PowerShell Script file: WinCertScripts.ps1"); }

            _logger.LogTrace($"Script file located here: {scriptFileLocation}");

            if (!isLocalMachine)
            {
                InitializeRemoteSession();
            }
            else
            {
                InitializeLocalSession();
            }

            // Display Hosting information
            string psInfo = @"
                    $psVersion = $PSVersionTable.PSVersion
                    $os = [System.Environment]::OSVersion
                    $hostName = [System.Net.Dns]::GetHostName()

                    [PSCustomObject]@{
                        PowerShellVersion = $psVersion
                        OperatingSystem   = $os
                        HostName          = $hostName
                    } | ConvertTo-Json
                ";
            var results = ExecutePowerShell(psInfo,isScript:true);
            foreach (var result in results)
            {
                _logger.LogTrace($"{result}");
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
                    { "UserKnownHostsFile", "/dev/null" },
                };

                PS.AddCommand("New-PSSession")
                    .AddParameter("HostName", ClientMachineName)
                    .AddParameter("UserName", serverUserName)
                    .AddParameter("KeyFilePath", tempKeyFilePath)
                    .AddParameter("ConnectingTimeout", 10000)
                    .AddParameter("Options", options);
            }
            else
            {
                _logger.LogTrace("Initializing WinRM connection");
                try
                {
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
                catch (Exception)
                {
                    throw new Exception("Problems establishing network credentials.  Please check the User name and Password for the Certificate Store");
                }

            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
                _logger.LogTrace("Script loaded into remote session successfully.");
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
                try
                {
                    PS.AddCommand("Remove-PSSession").AddParameter("Session", _PSSession);
                    PS.Invoke();
                    CheckErrors();
                }
                catch (Exception)
                {
                }
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

        public Collection<PSObject>? InvokeFunction(string functionName, Dictionary<string, Object>? parameters = null)
        {
            PS.Commands.Clear();

            // Prepare the command
            PS.AddCommand("Invoke-Command")
              .AddParameter("ScriptBlock", ScriptBlock.Create(functionName));

            if (!isLocalMachine)
            {
                PS.AddParameter("Session", _PSSession);
            }

            // Add parameters
            if (parameters != null)
            {
                PS.AddParameter("ArgumentList", parameters.Values.ToArray());
            }

            _logger.LogTrace($"Attempting to InvokeFunction: {functionName}");
            var results = PS.Invoke();

            if (PS.HadErrors)
            {
                string errorMessages = string.Join("; ", PS.Streams.Error.Select(e => e.ToString()));
                throw new Exception($"Error executing function '{functionName}': {errorMessages}");
            }

            return results;
        }

        public Collection<PSObject> ExecutePowerShellScript(string script)
        {
            PS.AddScript(script);
            return PS.Invoke();
        }


        public Collection<PSObject>? ExecutePowerShell(string commandOrScript, Dictionary<string, object>? parameters = null, bool isScript = false)
        {
            try
            {
                PS.Commands.Clear();

                // Handle Local or Remote Execution
                if (isLocalMachine)
                {
                    if (isScript)
                    {
                        // Add script content directly for local execution
                        PS.AddScript(commandOrScript);
                    }
                    else
                    {
                        // Add command for local execution
                        PS.AddCommand(commandOrScript);
                    }
                }
                else
                {
                    // For remote execution, use Invoke-Command
                    var scriptBlock = isScript
                        ? ScriptBlock.Create(commandOrScript) // Use the script as a ScriptBlock
                        : ScriptBlock.Create($"& {{ {commandOrScript} }}"); // Wrap commands in ScriptBlock

                    PS.AddCommand("Invoke-Command")
                      .AddParameter("Session", _PSSession)
                      .AddParameter("ScriptBlock", scriptBlock);
                }

                // Add Parameters if provided
                if (parameters != null)
                {
                    if (isLocalMachine || isScript)
                    {
                        foreach (var param in parameters)
                        {
                            PS.AddParameter(param.Key, param.Value);
                        }
                    }
                    else
                    {
                        // Remote execution: Use ArgumentList for parameters
                        var paramBlock = string.Join(", ", parameters.Select(p => $"[{p.Value.GetType().Name}] ${p.Key}"));
                        var paramUsage = string.Join(" ", parameters.Select(p => $"-{p.Key} ${p.Key}"));

                        string scriptBlockWithParams = $@"
                    param({paramBlock})
                    {commandOrScript} {paramUsage}
                ";

                        PS.Commands.Clear(); // Clear previous commands
                        PS.AddCommand("Invoke-Command")
                          .AddParameter("Session", _PSSession)
                          .AddParameter("ScriptBlock", ScriptBlock.Create(scriptBlockWithParams))
                          .AddParameter("ArgumentList", parameters.Values.ToArray());
                    }
                }

                // Log and execute
                _logger.LogTrace($"Executing PowerShell: {commandOrScript}");
                var results = PS.Invoke();

                // Check for errors
                if (PS.HadErrors)
                {
                    string errorMessages = string.Join("; ", PS.Streams.Error.Select(e => e.ToString()));
                    throw new Exception($"PowerShell execution errors: {errorMessages}");
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error executing PowerShell: {ex.Message}");
                throw;
            }
            finally
            {
                PS.Commands.Clear();
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

        private string createPrivateKeyFile()
        {
            string tmpFile = Path.GetTempFileName();  // "logs/AdminFile";
            _logger.LogTrace($"Created temporary KeyFilePath: {tmpFile}, writing bytes.");

            File.WriteAllText(tmpFile, formatPrivateKey(serverPassword));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogTrace($"Changing permissions on Windows temp file: {tmpFile}.");

                // Create a FileInfo object for the file
                FileInfo fileInfo = new FileInfo(tmpFile);

                // Get the current access control settings of the file
                FileSecurity fileSecurity = fileInfo.GetAccessControl();

                // Remove existing permissions
                fileSecurity.RemoveAccessRuleAll(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl, AccessControlType.Allow));

                // Grant read permissions to the current user
                string currentUser = Environment.UserName;
                fileSecurity.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.Read, AccessControlType.Allow));

                // Deny all access to others (this is optional, depending on your use case)
                fileSecurity.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.Read, AccessControlType.Deny));

                // Apply the modified permissions to the file
                fileInfo.SetAccessControl(fileSecurity);
                //File.SetAttributes(tmpFile, FileAttributes.ReadOnly);
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
    }
}
