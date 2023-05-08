@echo off

cd C:\Users\bhill\source\repos\iis-orchestrator\WinCertTestConsole\bin\Debug\netcoreapp3.1
set ClientMachine=iisbindingstest.command.local
set user=null
Set password=null
set storepath=My

echo ***********************************
echo Starting Management Test Cases
echo ***********************************
set casename=Management


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC1 %mgt% with no biding information.  Should do the %mgt% but give you a warning about missing bindings *not* trusted root
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=* -port=443 -hostname= -sitename=FirstSite -snicert="0 - No SNI" -protocol=https -overwrite=%overwrite%

echo:
echo ***********************************
echo Starting Inventory Test Cases
echo ***********************************


set casename=Inventory
echo:
echo *************************************************************************
echo TC22 Inventory Panorama Certificates from Trusted Root and Cert Locations
echo *************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo store path: %storepath%
echo group name: %devicegroup%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%clientmachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -devicegroup=%devicegroup% -managementtype=%mgt%

@pause
