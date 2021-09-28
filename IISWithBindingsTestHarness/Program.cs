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

            var connInfo =
                new WSManConnectionInfo(
                    new Uri($"http://localhost:5985/wsman"));

            connInfo.IncludePortInSPN = false;
            var pw = new NetworkCredential("keyfactor\\administrator", "Password1")
                .SecurePassword;
            connInfo.Credential = new PSCredential("keyfactor\\administrator", pw);

            using var runSpace = RunspaceFactory.CreateRunspace(connInfo);
            runSpace.Open();
            using var ps = PowerShell.Create();
            ps.Runspace = runSpace;

            ps.AddCommand("Import-Module")
                .AddParameter("Name", "WebAdministration")
                .AddStatement();

            //ps.AddCommand("Get-Website");
            var funcScript =
                "Foreach($Site in get-website) { Foreach ($Bind in $Site.bindings.collection) {[pscustomobject]@{name=$Site.name;Protocol=$Bind.Protocol;Bindings=$Bind.BindingInformation;thumbprint=$Bind.certificateHash}}}";
            ps.AddScript(funcScript).AddStatement();
            var bindings = ps.Invoke();

            foreach (var binding in bindings)
            {
                var siteName = binding.Properties["name"].Value.ToString();
                var ipAddress = binding.Properties["Bindings"].Value.ToString().Split(':')[0];
                var port = binding.Properties["Bindings"].Value.ToString().Split(':')[1];
                var hostName = binding.Properties["Bindings"].Value.ToString().Split(':')[2];
                var protocal= binding.Properties["Protocol"].Value.ToString();
                var thumbPrint = binding.Properties["thumbprint"].Value.ToString();
                Console.WriteLine(thumbPrint);
            }

        }
    }
}
