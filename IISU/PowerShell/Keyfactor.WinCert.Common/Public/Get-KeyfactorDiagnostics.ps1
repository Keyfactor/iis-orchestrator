function Get-KeyfactorDiagnostics {
    [CmdletBinding()]
    param()

    $InformationPreference = 'Continue'
    $separator = '=' * 70
    $subSep    = '-' * 70

    $isConstrained = $ExecutionContext.SessionState.LanguageMode -eq 'ConstrainedLanguage'
    $isRemote      = $null -ne $PSSenderInfo

    #region Header
    Write-Information $separator
    Write-Information "  Keyfactor Diagnostics Report"
    Write-Information "  Generated : $(Get-Date)"
    if ($isRemote)      { Write-Information "  Mode      : Remote Session" }
    if ($isConstrained) { Write-Information "  *** Running in Constrained Language Mode (JEA) ***" }
    if ($isConstrained) { Write-Information "  *** Some sections will be skipped or limited    ***" }
    Write-Information $separator
    #endregion

    #region Identity
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Identity"
    Write-Information $subSep
    Write-Information "  User         : $(whoami)"
    Write-Information "  Display Name : $($env:USERNAME)"
    Write-Information "  Domain       : $($env:USERDOMAIN)"
    Write-Information "  Computer     : $($env:COMPUTERNAME)"
    Write-Information "  PS Version   : $($PSVersionTable.PSVersion)"
    Write-Information "  PS Edition   : $($PSVersionTable.PSEdition)"
    Write-Information "  OS           : $($PSVersionTable.OS)"

    if (-not $isConstrained) {
        $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Write-Information "  Run As Admin : $isAdmin"
    } else {
        Write-Information "  Run As Admin : N/A (Constrained Language Mode)"
    }
    #endregion

    #region Session Info
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Session Information"
    Write-Information $subSep
    Write-Information "  Is Remote Session    : $isRemote"
    Write-Information "  Runspace Id          : $([System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.Id)"
    Write-Information "  Execution Policy     : $(Get-ExecutionPolicy)"
    Write-Information "  Language Mode        : $($ExecutionContext.SessionState.LanguageMode)"

    if ($PSSenderInfo) {
        Write-Information "  Connected User       : $($PSSenderInfo.UserInfo.Identity.Name)"
        Write-Information "  Connection String    : $($PSSenderInfo.ConnectionString)"
    }
    #endregion

    #region JEA
    Write-Information ""
    Write-Information $subSep
    Write-Information "  JEA (Just Enough Administration)"
    Write-Information $subSep
    $jeaConfigs = Get-PSSessionConfiguration -ErrorAction SilentlyContinue
    if ($jeaConfigs) {
        foreach ($config in $jeaConfigs) {
            Write-Information "  Endpoint             : $($config.Name)"
            Write-Information "    Enabled            : $($config.Enabled)"
            Write-Information "    Permission         : $($config.Permission)"
            Write-Information "    PSVersion          : $($config.PSVersion)"
            Write-Information "    Run As User        : $($config.RunAsUser)"
            Write-Information "    Session Type       : $($config.SessionType)"
            Write-Information "    Language Mode      : $($config.LanguageMode)"
            Write-Information "    Startup Script     : $($config.StartupScript)"
            Write-Information "    Role Definitions   : $($config.RoleDefinitions)"
            Write-Information ""
        }
    } else {
        Write-Information "  No PSSession configurations found or access denied."
    }
    #endregion

    #region WinRM
    Write-Information ""
    Write-Information $subSep
    Write-Information "  WinRM Service"
    Write-Information $subSep
    $winrm = Get-Service -Name WinRM -ErrorAction SilentlyContinue
    Write-Information "  WinRM Status         : $($winrm.Status)"
    Write-Information "  WinRM StartType      : $($winrm.StartType)"

    $winrmConfig = winrm get winrm/config 2>&1
    if ($winrmConfig -notmatch 'error') {
        $maxShells  = ($winrmConfig | Select-String 'MaxShellsPerUser')    -replace '.*=\s*', ''
        $maxMemory  = ($winrmConfig | Select-String 'MaxMemoryPerShellMB') -replace '.*=\s*', ''
        $maxTimeout = ($winrmConfig | Select-String 'MaxTimeoutms')        -replace '.*=\s*', ''
        Write-Information "  Max Shells/User      : $maxShells"
        Write-Information "  Max Memory (MB)      : $maxMemory"
        Write-Information "  Max Timeout (ms)     : $maxTimeout"
    } else {
        Write-Information "  Could not retrieve WinRM config (may require elevation)."
    }

    Write-Information ""
    Write-Information "  Listeners:"
    $listeners = Get-ChildItem WSMan:\localhost\Listener -ErrorAction SilentlyContinue
    foreach ($listener in $listeners) {
        $props = Get-Item "WSMan:\localhost\Listener\$($listener.PSChildName)\*" -ErrorAction SilentlyContinue
        Write-Information "    [$($listener.PSChildName)]"
        foreach ($prop in $props) {
            Write-Information "      $($prop.Name.PadRight(20)): $($prop.Value)"
        }
    }
    #endregion

    #region Network
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Network / Connectivity"
    Write-Information $subSep
    Write-Information "  WinRM HTTP  (5985) : $(Test-NetConnection -ComputerName localhost -Port 5985 -WarningAction SilentlyContinue | Select-Object -ExpandProperty TcpTestSucceeded)"
    Write-Information "  WinRM HTTPS (5986) : $(Test-NetConnection -ComputerName localhost -Port 5986 -WarningAction SilentlyContinue | Select-Object -ExpandProperty TcpTestSucceeded)"
    #endregion

    #region Firewall
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Firewall Rules (WinRM)"
    Write-Information $subSep
    $fwRules = Get-NetFirewallRule -DisplayGroup 'Windows Remote Management' -ErrorAction SilentlyContinue
    if ($fwRules) {
        foreach ($rule in $fwRules) {
            Write-Information "  $($rule.DisplayName.PadRight(45)) Enabled: $($rule.Enabled)  Action: $($rule.Action)  Direction: $($rule.Direction)"
        }
    } else {
        Write-Information "  No WinRM firewall rules found or access denied."
    }
    #endregion

    #region Group Memberships
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Group Memberships"
    Write-Information $subSep
    $groups = whoami /groups /fo csv | ConvertFrom-Csv
    foreach ($group in $groups) {
        $name = if ([string]::IsNullOrWhiteSpace($group.'Group Name')) { $group.SID } else { $group.'Group Name' }
        Write-Information "  $($name.PadRight(50)) SID: $($group.SID)"
    }
    #endregion

    #region Privileges
    Write-Information ""
    Write-Information $subSep
    Write-Information "  User Privileges"
    Write-Information $subSep
    $privs = whoami /priv /fo csv | ConvertFrom-Csv
    foreach ($priv in $privs) {
        Write-Information "  $($priv.'Privilege Name'.PadRight(45)) State: $($priv.State)"
    }
    #endregion

    #region Environment Variables
    Write-Information ""
    Write-Information $subSep
    Write-Information "  Relevant Environment Variables"
    Write-Information $subSep
    $relevantVars = @('PSModulePath', 'TEMP', 'TMP', 'PATH', 'PATHEXT', 'APPDATA', 'LOCALAPPDATA', 'SystemRoot')
    foreach ($var in $relevantVars) {
        Write-Information "  $($var.PadRight(20)): $([System.Environment]::GetEnvironmentVariable($var))"
    }
    #endregion

    Write-Information ""
    Write-Information $separator
    Write-Information "  End of Diagnostics Report"
    Write-Information $separator
}