using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;

namespace IISWithBindingsTestHarness
{
    class TestHarness
    {
        static void Main(string[] args)
        {

            try
            {
                //http://{config.CertificateStoreDetails.ClientMachine}:5985/wsman
                Uri RemoteComputerUri = new Uri("http://localhost:5985/WSMAN");
                WSManConnectionInfo connInfo = new WSManConnectionInfo(RemoteComputerUri);

                connInfo.IncludePortInSPN = true;
                SecureString pw = new NetworkCredential("keyfactor\\administrator", "Password1")
                    .SecurePassword;
                connInfo.Credential = new PSCredential("keyfactor\\administrator", pw);

                using (Runspace remoteRunspace = RunspaceFactory.CreateRunspace(connInfo))
                {
                    remoteRunspace.Open();
                }

            }
            catch(Exception e)
            {
                Console.Write(e.Message);
            }
        }
    }
}
