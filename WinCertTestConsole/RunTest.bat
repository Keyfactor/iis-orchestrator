@echo off

cd C:\Users\KFAdmin\source\repos\iis-orchestrator\WinCertTestConsole\bin\Debug\netcoreapp3.1
set ClientMachine=iisbindingstest.command.local
set user=KFAdmin
Set password=Wh5G2Tc6VBYjSMpC
set storepath=My

echo ***********************************
echo Starting Management Test Cases
echo ***********************************
set casename=Management

goto SNI

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC1 %mgt% new Cert To New Binding.  Should do the %mgt%, add the binding and add the cert to the binding
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=* -winrmport=5986 -hostname= -sitename=FirstSite -domain=www.fromtesttool.com -snicert="0 - No SNI" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC2 %mgt% /update Cert On Existing Binding.  Should do the %mgt%, and update the cert on the binding to the new cert
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=* -winrmport=5986 -hostname= -sitename=FirstSite -domain=www.fromtesttool2.com -snicert="0 - No SNI" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC3 %mgt% /update Cert set SNI.  Should do the %mgt%, the cert on the new binding to and Set SNI
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=* -winrmport=5986 -hostname=www.snitest.com -sitename=FirstSite -domain=www.fromtesttool2sni.com -iisport=443 -snicert="1 - SNI Enabled" -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC4 %mgt% new bind, new sni, new ip.  Should do the %mgt%, of the cert on new binding set new IP and Set SNI
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=10.3.10.12 -winrmport=5986 -hostname=www.snitest.com -sitename=FirstSite -domain=www.fromtesttool2sni.com  -iisport=443 -snicert="1 - SNI Enabled" -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC5 %mgt% new bind, new sni, same ip.  Should do the %mgt%, of the cert on new binding set same IP and Set SNI new host
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=10.3.10.12 -winrmport=5986 -hostname=www.tc5.com -sitename=FirstSite -domain=www.fromtesttool2sni.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC6 %mgt% new bind, same ip, same host, new port. Should do the %mgt%, of the cert on new binding b/c different port
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=10.3.10.12 -winrmport=5986 -hostname=www.tc5.com -sitename=FirstSite -domain=www.fromtesttool2sni.com -snicert="1 - SNI Enabled" -iisport=4443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=remove
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC7 %mgt% remove TC6 Cert. Should do the %mgt%, of the cert
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=10.3.10.12 -winrmport=5986 -hostname=www.tc5.com -sitename=FirstSite -domain=www.fromtesttool2sni.com -snicert="1 - SNI Enabled" -iisport=4443 -protocol=https -overwrite=%overwrite% -setupcert=false

echo:
echo ***********************************
echo Starting Renewal Test Cases
echo ***********************************

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC8 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.renewtest1.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC8 Setup %mgt% renewal setup, installing Cert to Site 2
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.renewtestsite1.com -sitename=SecondSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC8 %mgt% renewal cert binded in TC8 and TC9 with another cert, should find all thumprints and replace
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=true -ipaddress= -winrmport=5986 -hostname=www.renewtestsite2.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC9 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.firstsitebinding1.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC9 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.firstsitebinding2.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC9 %mgt% renewal cert binded in TC9 with another cert, should find all thumprints and replace
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=true -ipaddress= -winrmport=5986 -hostname=www.renewtestcase9.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC10 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.tc10a.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC10 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname=www.tc10b.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC10 %mgt% renewal cert binded in TC10 with another cert, should find all thumprints and replace
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=true -ipaddress= -winrmport=5986 -hostname=www.tc10b.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC11 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=192.168.58.162 -winrmport=5986 -hostname=www.tc11a.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC11 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=192.168.58.160 -winrmport=5986 -hostname=www.tc11a.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC11 %mgt% renewal cert binded in TC11 with another cert, should find all thumprints and replace
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=true -ipaddress= -winrmport=5986 -hostname=www.tc11b.com -sitename=FirstSite -domain=www.renewthis11b.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC12 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=192.168.58.161 -winrmport=5986 -hostname=www.tc11a.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=true


set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC12 Setup %mgt% renewal setup, installing Cert to Site 1
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress=192.168.58.161 -winrmport=5986 -hostname=www.tc11a.com -sitename=FirstSite -domain=www.renewthis.com -snicert="1 - SNI Enabled" -iisport=4423 -protocol=https -overwrite=%overwrite% -setupcert=true

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC12 %mgt% renewal cert binded in TC12 with another cert, should find all thumprints and replace
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=true -ipaddress= -winrmport=5986 -hostname=www.tc11b.com -sitename=FirstSite -domain=www.renewthis11b.com -snicert="1 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

:SNI

set cert=%random%
set casename=Management
set mgt=add
set trusted=false
set overwrite=false

echo ************************************************************************************************************************
echo TC15 %mgt% Default Web Site Blank Host Name
echo ************************************************************************************************************************
echo overwrite: %overwrite%
echo trusted: %trusted%
echo cert name: %cert%

WinCertTestConsole.exe -clientmachine=%ClientMachine% -casename=%casename% -user=%user% -password=%password% -storepath=%storepath% -managementtype=%mgt% -isrenew=false -ipaddress= -winrmport=5986 -hostname= -sitename=Default Web Site -domain=www.renewthis15.com -snicert="0 - SNI Enabled" -iisport=443 -protocol=https -overwrite=%overwrite% -setupcert=false

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
