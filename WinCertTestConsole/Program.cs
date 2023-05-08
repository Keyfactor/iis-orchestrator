using Keyfactor.Orchestrators.Extensions;
//using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win;
//using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinIIS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Newtonsoft.Json;
using Moq;
using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace WinCertTestConsole
{
    internal class Program
    {
        public static string UserName { get; set; }
        public static string Password { get; set; }
        public static string CaseName { get; set; }
        public static string CertAlias { get; set; }
        public static string ClientMachine { get; set; }
        public static string StorePath { get; set; }
        public static string VirtualServerName { get; set; }
        public static string KeyPairName { get; set; }
        public static string Overwrite { get; set; }
        public static string Renewal { get; set; }
        public static string Domain { get; set; }
        public static string SniCert { get; set; }
        public static string ManagementType { get; set; }
        public static string CertificateContent { get; set; }
        public static string Protocol { get; set; }
        public static string SiteName { get; set; }
        public static string HostName { get; set; }
        public static string Port { get; set; }
        public static string IpAddress { get; set; }


#pragma warning disable 1998
        private static async Task Main(string[] args)
#pragma warning restore 1998
        {


            var arguments = new Dictionary<string, string>();
            Thread.Sleep(10000);
            foreach (var argument in args)
            {
                var splitted = argument.Split('=');

                if (splitted.Length == 2) arguments[splitted[0]] = splitted[1];
            }
            if (args.Length > 0)
            {
                CaseName = arguments["-casename"];
                UserName = arguments["-user"];
                Password = arguments["-password"];
                StorePath = arguments["-storepath"];
                ClientMachine = arguments["-clientmachine"];
            }

            // Display message to user to provide parameters.
            Console.WriteLine("Running");

            switch (CaseName)
            {
                case "Inventory":
                    Console.WriteLine("Running Inventory");
                    InventoryJobConfiguration invJobConfig;
                    invJobConfig = GetInventoryJobConfiguration();
                    Console.WriteLine("Got Inventory Config");
                    SubmitInventoryUpdate sui = GetItems;
                    var secretResolver = new Mock<IPAMSecretResolver>();
                    secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerUsername)))
                        .Returns(() => invJobConfig.ServerUsername);
                    secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerPassword)))
                        .Returns(() => invJobConfig.ServerPassword);
                    var inv = new Inventory(secretResolver.Object);
                    Console.WriteLine("Created Inventory Object With Constructor");
                    var invResponse = inv.ProcessJob(invJobConfig, sui);
                    Console.WriteLine("Back From Inventory");
                    Console.Write(JsonConvert.SerializeObject(invResponse));
                    Console.ReadLine();
                    break;
                case "Management":
                    Console.WriteLine("Select Management Type Add or Remove");
                    string mgmtType;
                    mgmtType = args.Length == 0 ? Console.ReadLine() : arguments["-managementtype"];

                    if (mgmtType?.ToUpper() == "ADD")
                    {
                        if (args.Length > 0)
                        {
                            IpAddress = arguments["-ipaddress"];
                            Port = arguments["-port"];
                            Overwrite = arguments["-overwrite"];
                            Renewal = arguments["-isrenew"];
                            HostName = arguments["-hostname"];
                            SiteName = arguments["-sitename"];
                            SniCert = arguments["-snicert"];
                            Protocol = arguments["-protocol"];
                        }

                        Console.WriteLine("Start Generated Cert in KF API");
                        var client = new KeyfactorClient();
                        var kfResult = client.EnrollCertificate($"{Domain}").Result;
                        CertificateContent = kfResult.CertificateInformation.Pkcs12Blob;
                        Console.WriteLine("End Generated Cert in KF API");

                        if (Renewal.ToUpper() == "FALSE")
                            SetRenewThumbprint(kfResult.CertificateInformation.Thumbprint);

                        var jobConfiguration = GetManagementJobConfiguration(Renewal.ToUpper() == "TRUE" ? "ManagementRenewModified" : "Management");

                        var mgmtSecretResolver = new Mock<IPAMSecretResolver>();
                        mgmtSecretResolver
                            .Setup(m => m.Resolve(It.Is<string>(s => s == jobConfiguration.ServerUsername)))
                            .Returns(() => jobConfiguration.ServerUsername);
                        mgmtSecretResolver
                            .Setup(m => m.Resolve(It.Is<string>(s => s == jobConfiguration.ServerPassword)))
                            .Returns(() => jobConfiguration.ServerPassword);
                        var mgmt = new Management(mgmtSecretResolver.Object);


                        var result = mgmt.ProcessJob(jobConfiguration);
                        Console.Write(JsonConvert.SerializeObject(result));
                        Console.ReadLine();
                    }

                    if (mgmtType != null && mgmtType.ToUpper() == "REMOVE")
                    {
                        if (args.Length > 0)
                        {
                            CertAlias = arguments["-certalias"];
                        }

                        var jobConfig = GetRemoveJobConfiguration();

                        var mgmtSecretResolver = new Mock<IPAMSecretResolver>();
                        mgmtSecretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == jobConfig.ServerUsername)))
                            .Returns(() => jobConfig.ServerUsername);
                        mgmtSecretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == jobConfig.ServerPassword)))
                            .Returns(() => jobConfig.ServerPassword);
                        var mgmt = new Management(mgmtSecretResolver.Object);
                        var result = mgmt.ProcessJob(jobConfig);
                        Thread.Sleep(5000);
                        Console.Write(JsonConvert.SerializeObject(result));
                        Console.ReadLine();
                    }

                    break;
            }
        }



        public static bool GetItems(IEnumerable<CurrentInventoryItem> items)
        {
            return true;
        }


        public static InventoryJobConfiguration GetInventoryJobConfiguration()
        {
            var fileContent = File.ReadAllText("Inventory.json").Replace("UserNameGoesHere", UserName)
                .Replace("PasswordGoesHere", Password).Replace("TemplateNameGoesHere", StorePath)
                .Replace("ClientMachineGoesHere", ClientMachine);
            var result =
                JsonConvert.DeserializeObject<InventoryJobConfiguration>(fileContent);
            return result;
        }

        public static ManagementJobConfiguration GetManagementJobConfiguration(string fileName)
        {

            var overWriteReplaceString = "\"Overwrite\": false";
            if (Overwrite.ToUpper() == "TRUE")
            {
                overWriteReplaceString = "\"Overwrite\": true";
            }

            var hostNameReplaceString = "\"HostName\": null";
            if (!string.IsNullOrEmpty(HostName))
                hostNameReplaceString = $"\"HostName\": \"{HostName}\"";
            
            var fileContent = File.ReadAllText($"{fileName}.json").Replace("UserNameGoesHere", UserName)
                .Replace("PasswordGoesHere", Password).Replace("TemplateNameGoesHere", StorePath)
                .Replace("AliasGoesHere", CertAlias)
                .Replace("ClientMachineGoesHere", ClientMachine)
                .Replace("PortGoesHere", Port)
                .Replace("IPAddressGoesHere", IpAddress)
                .Replace("SiteNameGoesHere", SiteName)
                .Replace("ProtocolGoesHere", Protocol)
                .Replace("SniFlagGoesHere", SniCert)
                .Replace("\"HostName\": null", hostNameReplaceString)
                .Replace("\"Overwrite\": false", overWriteReplaceString)
                .Replace("CertificateContentGoesHere", CertificateContent);
            var result =
                JsonConvert.DeserializeObject<ManagementJobConfiguration>(fileContent);
            return result;
        }

        public static void SetRenewThumbprint(string lastThumbprint)
        {
            var fileContent = File.ReadAllText("ManagementRenew.json")
                .Replace("ThumbprintGoesHere", lastThumbprint);
            File.WriteAllText("ManagementRenewModified.json", fileContent, Encoding.UTF8);
        }


        public static ManagementJobConfiguration GetRemoveJobConfiguration()
        {
            var fileContent = File.ReadAllText("ManagementRemove.json").Replace("UserNameGoesHere", UserName)
                .Replace("PasswordGoesHere", Password).Replace("TemplateNameGoesHere", StorePath)
                .Replace("AliasGoesHere", CertAlias)
                .Replace("ClientMachineGoesHere", ClientMachine)
                .Replace("virtualServerNameGoesHere", VirtualServerName);
            var result =
                JsonConvert.DeserializeObject<ManagementJobConfiguration>(fileContent);
            return result;
        }
    }
}
