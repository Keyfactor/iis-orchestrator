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

// Ignore Spelling: Keyfactor

using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
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
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        private string? argument;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

        private string serverUserName;
        private string serverPassword;

        private bool isLocalMachine;
        private bool isADFSStore = false;

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

        public PSHelper()
        {
            // Empty constructor for unit testing
            _logger = LogHandler.GetClassLogger<PSHelper>();
        }

        public PSHelper(string protocol, string port, bool useSPN, string clientMachineName, string serverUserName, string serverPassword, bool isADFSStore = false)
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
            this.isADFSStore = isADFSStore;
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

            scriptFileLocation = FindScriptsDirectory(AppDomain.CurrentDomain.BaseDirectory, "PowerShellScripts");
            if (scriptFileLocation == null) { throw new Exception("Unable to find the accompanying PowerShell Script files,"); }

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
            var results = ExecutePowerShell(psInfo, isScript: true);
            foreach (var result in results)
            {
                _logger.LogTrace($"{result}");
            }
        }

        private void InitializeRemoteSession()
        {
            if (this.isADFSStore) throw new Exception("Remote ADFS stores are not supported.");

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
                    // Create the PSSessionOption object
                    var sessionOption = new PSSessionOption
                    {
                        IncludePortInSPN = useSPN
                    };

                    PS.AddCommand("New-PSSession")
                    .AddParameter("ComputerName", ClientMachineName)
                    .AddParameter("Port", port)
                    .AddParameter("SessionOption", sessionOption);

                    if (protocol == "https")
                    {
                        _logger.LogTrace($"Using HTTPS to connect to: {clientMachineName}");
                        PS.AddParameter("UseSSL");
                    }

                    if (!string.IsNullOrEmpty(serverUserName))
                    {
                        var pw = new NetworkCredential(serverUserName, serverPassword).SecurePassword;
                        PSCredential myCreds = new PSCredential(serverUserName, pw);

                        PS.AddParameter("Credential", myCreds);
                    }

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
                    .AddParameter("ScriptBlock", ScriptBlock.Create(LoadAllScripts(scriptFileLocation)));

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
            _logger.LogTrace("Creating out-of-process Powershell Runspace.");
            PowerShellProcessInstance psInstance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
            Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), psInstance);
            rs.Open();
            PS.Runspace = rs;

            // Set execution policy
            _logger.LogTrace("Setting Execution Policy to Unrestricted");
            SetExecutionPolicyUnrestricted();

            // Check if ADFS module is available (only needed for ADFS stores)
            bool adfsModuleImported = false;
            if (this.isADFSStore)
            {
                adfsModuleImported = ImportAdfsModule();
            }

            // Load all scripts
            _logger.LogTrace("Loading PowerShell scripts");
            var scriptFiles = GetScriptFiles(scriptFileLocation);

            foreach (var scriptFile in scriptFiles)
            {
                var fileName = Path.GetFileName(scriptFile);
                bool isAdfsScript = fileName.IndexOf("adfs", StringComparison.OrdinalIgnoreCase) >= 0;

                // Decide whether to load this script
                if (isAdfsScript)
                {
                    if (this.isADFSStore)
                    {
                        if (!adfsModuleImported)
                        {
                            _logger.LogWarning($"Skipping ADFS script '{fileName}' - ADFS module not available");
                            continue;
                        }

                        _logger.LogTrace($"Loading ADFS script: {fileName}");
                    }
                    else
                    {
                        _logger.LogTrace($"Skipping ADFS script '{fileName}' - not an ADFS store");
                        continue;
                    }
                }
                else
                {
                    _logger.LogTrace($"Loading script: {fileName}");
                }

                // Load the script
                try
                {
                    PS.AddScript($". '{scriptFile}'");
                    PS.Invoke();

                    if (PS.HadErrors)
                    {
                        _logger.LogError($"Errors loading script '{fileName}':");
                        foreach (var error in PS.Streams.Error)
                        {
                            _logger.LogError($"  {error}");
                        }
                    }
                    else
                    {
                        _logger.LogTrace($"  ✓ Successfully loaded {fileName}");
                    }

                    CheckErrors();
                    PS.Commands.Clear();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception loading script '{fileName}': {ex.Message}");
                }
            }

            _logger.LogInformation("Local PowerShell session initialized successfully");
        }

        /// <summary>
        /// Import ADFS module if available
        /// </summary>
        /// <returns>True if module imported successfully, false otherwise</returns>
        private bool ImportAdfsModule()
        {
            _logger.LogTrace("Attempting to import ADFS module...");

            try
            {
                // First check if module is available
                PS.AddScript("Get-Module -ListAvailable -Name ADFS");
                var availableModules = PS.Invoke();

                if (availableModules == null || availableModules.Count == 0)
                {
                    _logger.LogWarning("ADFS module not found on this machine");
                    _logger.LogWarning("This may not be an ADFS server or ADFS role is not installed");
                    PS.Commands.Clear();
                    return false;
                }

                PS.Commands.Clear();

                // Module is available, import it
                _logger.LogTrace("ADFS module found, importing...");
                PS.AddCommand("Import-Module")
                    .AddParameter("Name", "ADFS")
                    .AddParameter("ErrorAction", "Stop");

                var moduleResult = PS.Invoke();

                if (PS.HadErrors)
                {
                    _logger.LogWarning("ADFS module import had errors:");
                    foreach (var error in PS.Streams.Error)
                    {
                        _logger.LogWarning($"  {error}");
                    }
                    PS.Streams.Error.Clear();
                    PS.Commands.Clear();
                    return false;
                }

                PS.Commands.Clear();

                // Verify module loaded
                PS.AddScript("Get-Module -Name ADFS");
                var loadedModules = PS.Invoke();

                if (loadedModules != null && loadedModules.Count > 0)
                {
                    var module = loadedModules[0];
                    var version = module.Properties["Version"]?.Value?.ToString();
                    _logger.LogInformation($"✓ ADFS module imported successfully (Version: {version})");
                    PS.Commands.Clear();
                    return true;
                }
                else
                {
                    _logger.LogWarning("ADFS module import reported success but module not loaded");
                    PS.Commands.Clear();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not import ADFS module: {ex.Message}");
                _logger.LogWarning("ADFS cmdlets may not be available");

                try
                {
                    PS.Commands.Clear();
                }
                catch { }

                return false;
            }
        }
        private void SetExecutionPolicyUnrestricted()
        {
            try
            {
                PS.AddScript("Set-ExecutionPolicy Unrestricted -Scope Process -Force");
                PS.Invoke();

                // Check if there were any errors
                if (PS.HadErrors)
                {
                    foreach (var error in PS.Streams.Error)
                    {
                        var errorMsg = error.ToString();

                        // Execution policy messages are informational, not errors
                        if (errorMsg.Contains("execution policy successfully") ||
                            errorMsg.Contains("setting is overridden"))
                        {
                            _logger.LogInformation($"Execution Policy Info: {errorMsg}");
                        }
                        else
                        {
                            // Real error
                            _logger.LogError($"Execution Policy Error: {errorMsg}");
                            throw new Exception($"Failed to set execution policy: {errorMsg}");
                        }
                    }
                }

                _logger.LogTrace("Execution policy set successfully");
            }
            finally
            {
                // Always clear errors and commands
                PS.Streams.Error.Clear();
                PS.Commands.Clear();
            }
        }
        private void InitializeLocalSessionOLD2()
        {
            _logger.LogTrace("Creating out-of-process Powershell Runspace.");
            PowerShellProcessInstance psInstance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
            Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), psInstance);
            rs.Open();
            PS.Runspace = rs;

            // Set execution policy - ignore informational messages
            _logger.LogTrace("Setting Execution Policy to Unrestricted");
            SetExecutionPolicyUnrestricted();

            // Load all scripts
            _logger.LogTrace("Loading PowerShell scripts");
            var scriptFiles = GetScriptFiles(scriptFileLocation);
            _logger.LogInformation($"Found {scriptFiles.Count} script file(s) to load");

            foreach (var scriptFile in scriptFiles)
            {
                var fileName = Path.GetFileName(scriptFile);

                if (this.isADFSStore && fileName.ToLower().Contains("adfs"))
                {
                    // Import ADFS module (CRITICAL!)
                    _logger.LogTrace("Importing ADFS module");
                    try
                    {
                        PS.AddCommand("Import-Module").AddParameter("Name", "ADFS");
                        var moduleResult = PS.Invoke();

                        if (PS.HadErrors)
                        {
                            _logger.LogWarning("ADFS module import had errors (may not be available on this machine)");
                            foreach (var error in PS.Streams.Error)
                            {
                                _logger.LogWarning($"  {error}");
                            }
                            PS.Streams.Error.Clear();
                        }
                        else
                        {
                            _logger.LogInformation("ADFS module imported successfully");
                        }

                        PS.Commands.Clear();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not import ADFS module: {ex.Message}");
                        _logger.LogWarning("ADFS cmdlets may not be available");
                    }

                    _logger.LogTrace($"Skipping non-ADFS script: {fileName} for ADFS store type");
                    continue;
                }

                _logger.LogTrace($"Loading script: {fileName}");

                PS.AddScript($". '{scriptFile}'");
                PS.Invoke();
                CheckErrors();  // Check errors for actual scripts
                PS.Commands.Clear();
            }

            _logger.LogInformation("Local PowerShell session initialized successfully");
        }
        private void InitializeLocalSessionOLD()
        {
            _logger.LogTrace("Creating out-of-process Powershell Runspace.");
            PowerShellProcessInstance psInstance = new PowerShellProcessInstance(new Version(5, 1), null, null, false);
            Runspace rs = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(Array.Empty<string>()), psInstance);
            rs.Open();
            PS.Runspace = rs;

            _logger.LogTrace("Setting Execution Policy to Unrestricted");
            PS.AddScript("Set-ExecutionPolicy Unrestricted -Scope Process -Force");
            PS.Invoke();  // Ensure the script is invoked and loaded
            CheckErrors();

            PS.Commands.Clear();  // Clear commands after loading functions

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
                    if (_PSSession != null && _PSSession.Count > 0)
                    {
                        _logger.LogTrace("Removing remote PSSession.");
                        PS.AddCommand("Remove-PSSession").AddParameter("Session", _PSSession);
                        PS.Invoke();
                        CheckErrors();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Error while removing PSSession: {ex.Message}");
                }
            }

            if (File.Exists(tempKeyFilePath))
            {
                try
                {
                    File.Delete(tempKeyFilePath);
                    _logger.LogTrace($"Temporary KeyFilePath deleted: {tempKeyFilePath}");
                }
                catch (FileNotFoundException)
                {
                    _logger.LogTrace($"Temporary KeyFilePath was not found: {tempKeyFilePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Error while deleting KeyFilePath: {ex.Message}");
                }
            }

            try
            {
                PS.Runspace.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error while attempting to close the PowerShell Runspace: {ex.Message}");
            }

            PS.Dispose();
        }

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public Collection<PSObject>? InvokeFunctionOLD(string functionName, Dictionary<string, Object>? parameters = null)
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
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

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public Collection<PSObject>? InvokeFunction(string functionName, Dictionary<string, Object>? parameters = null)
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        {
            PS.Commands.Clear();

            if (isLocalMachine)
            {
                PS.AddCommand(functionName);
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        PS.AddParameter(param.Key, param.Value);
                    }
                }
            }
            else
            {
                string scriptBlock;

                if (parameters != null && parameters.Count > 0)
                {
                    // Build parameter list for param() block
                    var paramNames = parameters.Keys.Select(k => $"${k}").ToArray();
                    var paramBlock = string.Join(", ", paramNames);

                    // Build function call with named parameters
                    var functionCall = new System.Text.StringBuilder(functionName);
                    foreach (var param in parameters)
                    {
                        functionCall.Append($" -{param.Key} ${param.Key}");
                    }

                    // Create ScriptBlock with param() and function call
                    scriptBlock = $@"
                param({paramBlock})
                {functionCall}
            ";

                    _logger.LogTrace($"Remote ScriptBlock: {scriptBlock}");
                    _logger.LogTrace($"ArgumentList: {string.Join(", ", parameters.Keys)}");

                    PS.AddCommand("Invoke-Command")
                        .AddParameter("Session", _PSSession)
                        .AddParameter("ScriptBlock", ScriptBlock.Create(scriptBlock))
                        .AddParameter("ArgumentList", parameters.Values.ToArray());
                }
                else
                {
                    // No parameters - simple function call
                    scriptBlock = functionName;

                    PS.AddCommand("Invoke-Command")
                        .AddParameter("Session", _PSSession)
                        .AddParameter("ScriptBlock", ScriptBlock.Create(scriptBlock));
                }
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


#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public Collection<PSObject>? ExecutePowerShell(string commandOrScript, Dictionary<string, object>? parameters = null, bool isScript = false)
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
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
                if (parameters != null && parameters.Count > 0)
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
                        var paramBlock = string.Join(", ", parameters.Select(p =>
                        {
                            string typeName = p.Value?.GetType().Name ?? "object";
                            return $"[{typeName}] ${p.Key}";
                        }));

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

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public static void ProcessPowerShellScriptEvent(object? sender, DataAddedEventArgs e)
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
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
            string header = privateKey.Substring(0, privateKey.IndexOf("KEY-----") + 8);
            string footer = privateKey.Substring(privateKey.IndexOf("-----END"));

            return privateKey.Replace(header, "HEADER").Replace(footer, "FOOTER").Replace(" ", Environment.NewLine).Replace("HEADER", header).Replace("FOOTER", footer) + Environment.NewLine;
        }

        public static string FindScriptsDirectory(string rootDirectory, string directoryName)
        {
            /*
             * Searches for the scripts directory starting from searchRoot
             * 
             * Example:
             * FindScriptsDirectory(@"C:\Program Files\MyApp", "Scripts")
             * Returns: "C:\Program Files\MyApp\Scripts" (if found)
             */

            try
            {
                // Check if the current directory matches
                if (Path.GetFileName(rootDirectory)
                        .Equals(directoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return rootDirectory;
                }

                // Recurse into subdirectories
                foreach (string subDir in Directory.GetDirectories(rootDirectory))
                {
                    string result = FindScriptsDirectory(subDir, directoryName);
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories that cannot be accessed
            }
            catch (DirectoryNotFoundException)
            {
                // Skip directories that might have been deleted
            }

            return null;
        }
        private List<string> GetScriptFiles(string scriptFileLocation)
        {
            /*
             * Gets all .ps1 files from the scripts directory
             * 
             * scriptFileLocation can be:
             * - A file path: C:\MyApp\Scripts\WinCertScripts.ps1
             * - A directory path: C:\MyApp\Scripts
             * 
             * Returns: List of full file paths to all .ps1 files
             */

            // Determine the scripts directory
            string scriptsDirectory;

            if (File.Exists(scriptFileLocation))
            {
                // It's a file path - get the directory
                scriptsDirectory = Path.GetDirectoryName(scriptFileLocation);
                _logger.LogTrace($"Script file provided: {scriptFileLocation}");
                _logger.LogTrace($"Using directory: {scriptsDirectory}");
            }
            else if (Directory.Exists(scriptFileLocation))
            {
                // It's already a directory
                scriptsDirectory = scriptFileLocation;
                _logger.LogTrace($"Script directory provided: {scriptFileLocation}");
            }
            else
            {
                throw new DirectoryNotFoundException($"Scripts location not found: {scriptFileLocation}");
            }

            // Get all .ps1 files, excluding .example files
            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.ps1")
                .Where(f => !f.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (scriptFiles.Count == 0)
            {
                throw new FileNotFoundException($"No .ps1 files found in: {scriptsDirectory}");
            }

            _logger.LogTrace($"Found {scriptFiles.Count} script file(s): {string.Join(", ", scriptFiles.Select(Path.GetFileName))}");

            return scriptFiles;
        }
        public static string LoadScript(string scriptFileName)
        {
            _logger.LogTrace($"Attempting to load script {scriptFileName}");

            if (File.Exists(scriptFileName))
            {
                return File.ReadAllText(scriptFileName);
            }
            else
            { throw new Exception($"File: {scriptFileName} was not found."); }
        }
        public string LoadAllScripts(string scriptFileLocation)
        {
            /*
             * Loads all .ps1 files from the scripts directory into a single script string
             * 
             * scriptFileLocation can be:
             * - A file path: C:\MyApp\Scripts\WinCertScripts.ps1
             * - A directory path: C:\MyApp\Scripts
             * 
             * Returns: Combined script content of all .ps1 files
             */

            var scriptBuilder = new StringBuilder();

            // Determine the scripts directory
            string scriptsDirectory;
            if (File.Exists(scriptFileLocation))
            {
                // It's a file path - get the directory
                scriptsDirectory = Path.GetDirectoryName(scriptFileLocation);
                _logger.LogTrace($"Script file provided: {scriptFileLocation}");
            }
            else if (Directory.Exists(scriptFileLocation))
            {
                // It's already a directory
                scriptsDirectory = scriptFileLocation;
                _logger.LogTrace($"Script directory provided: {scriptFileLocation}");
            }
            else
            {
                throw new DirectoryNotFoundException($"Scripts location not found: {scriptFileLocation}");
            }

            _logger.LogInformation($"Loading scripts from: {scriptsDirectory}");

            // Load all .ps1 files from the scripts directory
            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.ps1").ToList();

            if (scriptFiles.Count == 0)
            {
                throw new FileNotFoundException($"No .ps1 files found in: {scriptsDirectory}");
            }

            _logger.LogInformation($"Found {scriptFiles.Count} script file(s) to load");

            // Load each script file
            foreach (var scriptFile in scriptFiles)
            {
                var fileName = Path.GetFileName(scriptFile);
                _logger.LogTrace($"Loading script: {fileName}");

                try
                {
                    var scriptContent = File.ReadAllText(scriptFile);

                    // Remove auto-initialization lines that won't work remotely
                    scriptContent = RemoveAutoInitialization(scriptContent);

                    scriptBuilder.AppendLine("# ============================================================================");
                    scriptBuilder.AppendLine($"# Script: {fileName}");
                    scriptBuilder.AppendLine("# ============================================================================");
                    scriptBuilder.AppendLine(scriptContent);
                    scriptBuilder.AppendLine();
                    scriptBuilder.AppendLine($"# --- End of {fileName} ---");
                    scriptBuilder.AppendLine();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to load script {fileName}: {ex.Message}");
                    throw new Exception($"Failed to load script {fileName}: {ex.Message}", ex);
                }
            }

            scriptBuilder.AppendLine("# All scripts loaded.");

            var combinedScript = scriptBuilder.ToString();
            _logger.LogInformation($"Combined script size: {combinedScript.Length} characters ({scriptFiles.Count} files)");

            return combinedScript;
        }

        /// <summary>
        /// Removes auto-initialization lines that won't work in remote context
        /// </summary>
        private string RemoveAutoInitialization(string scriptContent)
        {
            var lines = scriptContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Remove lines that call Initialize-Extensions or similar initialization
            var filteredLines = lines.Where(line =>
            {
                var trimmedLine = line.Trim();

                // Skip initialization lines that depend on file system
                if (trimmedLine.Equals("Initialize-Extensions", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("Initialize-Extensions ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith(". $PSScriptRoot", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            });

            return string.Join(Environment.NewLine, filteredLines);
        }
    }
}
