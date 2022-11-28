#Step 1 on IIS Box Enable WinRM
winrm quickconfig

#Step 2 on the IIS Box Allow the Firewall
netsh advfirewall firewall add rule name="WinRM-HTTPS" dir=in localport=5986 protocol=TCP action=allow

#Step 3 on the IIS Box, create a self signed certificate and listener for that certificate
$c = New-SelfSignedCertificate -DnsName "KFTrain.keyfactor.lab" -CertStoreLocation cert:\LocalMachine\My
winrm create winrm/config/Listener?Address=*+Transport=HTTPS "@{Hostname=`"KFTrain.keyfactor.lab`";CertificateThumbprint=`"$($c.ThumbPrint)`"}"


#Step 3a on The IIS Box, Manually add this cert to Machine Trusted Store (If on the same box Step 5 will take care of this)


#Step 4 on Orchestrator Box, Test Connection to Port
Test-netConnection KFTrain.keyfactor.lab -Port 5986


#Step 5 on the Orchestrator Box Install the Self Signed Cert as trusted
$webRequest = [Net.WebRequest]::Create("https://KFTrain.keyfactor.lab:5986/wsman")
try { $webRequest.GetResponse() } catch {}
$cert = $webRequest.ServicePoint.Certificate
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store `
  -ArgumentList  "Root", "LocalMachine"
$store.Open('ReadWrite')
$store.Add($cert)
$store.Close()

#Step 6 On Orchestrator Box, Test the SSL WSMan Connection
Test-WSMan -ComputerName IISDJ.keyfactor.lab -UseSSL 


#Other Troubleshooting Steps

#Get the Listener Instances
#Get-WSManInstance winrm/config/listener -Enumerate

#WinRm Quick Config
#winrm quickconfig -transport:https


#Delete HTTPS LISTENER
#winrm delete winrm/config/Listener?Address=*+Transport=HTTPS


#Get Full Config
#winrm get winrm/config

#Set Trusted Host for http
#winrm set winrm/config/client@{trustedhosts="%server1.thedemodrive.com%,%eccca.thedemodrive.com%"}
