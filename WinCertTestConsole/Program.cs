﻿// Copyright 2023 Keyfactor
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Moq;
using Newtonsoft.Json;

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
        public static string Overwrite { get; set; }
        public static string Renewal { get; set; }
        public static string Domain { get; set; }
        public static string SniCert { get; set; }
        public static string CertificateContent { get; set; }
        public static string Protocol { get; set; }
        public static string SiteName { get; set; }
        public static string HostName { get; set; }
        public static string Port { get; set; }
        public static string WinRmPort { get; set; }
        public static string IpAddress { get; set; }
        public static string IsSetupCert { get; set; }
        public static Dictionary<string, string> Arguments { get; set; }
        public static string[] Args { get; set; }

#pragma warning disable 1998
        private static async Task Main(string[] args)
#pragma warning restore 1998
        {
            Args = args;
            Arguments = new Dictionary<string, string>();
            Thread.Sleep(10000);
            foreach (var argument in args)
            {
                var splitted = argument.Split('=');

                if (splitted.Length == 2) Arguments[splitted[0]] = splitted[1];
            }

            if (args.Length > 0)
            {
                CaseName = Arguments["-casename"];
                UserName = Arguments["-user"];
                Password = Arguments["-password"];
                StorePath = Arguments["-storepath"];
                ClientMachine = Arguments["-clientmachine"];
                WinRmPort = Arguments["-winrmport"];
            }

            // Display message to user to provide parameters.
            Console.WriteLine("Running");

            // Short-cut settings for WinCert
            CaseName = "Inventory";
            UserName = "user4";
            Password = "Password1";
            StorePath = "My";
            ClientMachine = "192.168.230.170";
            WinRmPort = "5985";
            string StoreType = "WinCert";
            //

            switch (CaseName)
            {
                case "Inventory":
                    if (StoreType == "WinIIS")
                    {
                        Console.WriteLine("Running WinIIS Inventory");
                        InventoryJobConfiguration invJobConfig;
                        invJobConfig = GetInventoryJobConfiguration(StoreType);
                        Console.WriteLine("Got Inventory Config");
                        SubmitInventoryUpdate sui = GetItems;
                        var secretResolver = new Mock<IPAMSecretResolver>();
                        secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerUsername)))
                            .Returns(() => invJobConfig.ServerUsername);
                        secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerPassword)))
                            .Returns(() => invJobConfig.ServerPassword);
                        var inv = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU.Inventory(secretResolver.Object);
                        Console.WriteLine("Created Inventory Object With Constructor");
                        var invResponse = inv.ProcessJob(invJobConfig, sui);
                        Console.WriteLine("Back From Inventory");
                        Console.Write(JsonConvert.SerializeObject(invResponse));
                        Console.ReadLine();
                    }
                    else if(StoreType == "WinCert")
                    {
                        Console.WriteLine("Running WinCert Inventory");
                        InventoryJobConfiguration invJobConfig;
                        invJobConfig = GetInventoryJobConfiguration(StoreType);
                        Console.WriteLine("Got Inventory Config");
                        SubmitInventoryUpdate sui = GetItems;
                        var secretResolver = new Mock<IPAMSecretResolver>();
                        secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerUsername)))
                            .Returns(() => invJobConfig.ServerUsername);
                        secretResolver.Setup(m => m.Resolve(It.Is<string>(s => s == invJobConfig.ServerPassword)))
                            .Returns(() => invJobConfig.ServerPassword);
                        var inv = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinCert.Inventory(secretResolver.Object);
                        Console.WriteLine("Created Inventory Object With Constructor");
                        var invResponse = inv.ProcessJob(invJobConfig, sui);
                        Console.WriteLine("Back From Inventory");
                        Console.Write(JsonConvert.SerializeObject(invResponse));
                        Console.ReadLine();

                    }

                    break;

                case "Management":
                    Console.WriteLine("Select Management Type Add or Remove");
                    string mgmtType;
                    mgmtType = args.Length == 0 ? Console.ReadLine() : Arguments["-managementtype"];

                    if (mgmtType?.ToUpper() == "ADD")
                        ProcessManagementJob("Management");
                    else if (mgmtType?.ToUpper() == "REMOVE") ProcessManagementJob("Remove");

                    break;
            }
        }

        private static void ProcessManagementJob(string jobType)
        {
            if (Args.Length > 0)
            {
                IpAddress = Arguments["-ipaddress"];
                Port = Arguments["-iisport"];
                Overwrite = Arguments["-overwrite"];
                Renewal = Arguments["-isrenew"];
                HostName = Arguments["-hostname"];
                SiteName = Arguments["-sitename"];
                SniCert = Arguments["-snicert"];
                Protocol = Arguments["-protocol"];
                Domain = Arguments["-domain"];
                IsSetupCert = Arguments["-setupcert"];
            }

            Console.WriteLine($"Start Generated Cert in KF API for {jobType}");
            var client = new KeyfactorClient();
            var kfResult = client.EnrollCertificate($"{Domain}").Result;
            CertificateContent = kfResult.CertificateInformation.Pkcs12Blob;
            Console.WriteLine($"End Generated Cert in KF API for {jobType}");

            var isRenewal = Renewal.ToUpper() == "TRUE";
            var isSetup = IsSetupCert.ToUpper() == "TRUE";

            ManagementJobConfiguration jobConfiguration;

            if (!isSetup)
            {
                jobConfiguration = jobType.ToUpper() == "REMOVE"
                    ? GetRemoveJobConfiguration()
                    : GetManagementJobConfiguration("Management");

                if (isRenewal)
                {
                    var setupConfiguration = GetManagementJobConfiguration("RenewalSetup");
                    var renewalThumbprint = setupConfiguration.JobCertificate.Thumbprint;
                    jobConfiguration.JobProperties.Add("RenewalThumbprint", renewalThumbprint);
                }
            }
            else
            {
                jobConfiguration = GetManagementJobConfiguration("RenewalSetup");
            }


            var mgmtSecretResolver = new Mock<IPAMSecretResolver>();
            mgmtSecretResolver
                .Setup(m => m.Resolve(It.Is<string>(s => s == jobConfiguration.ServerUsername)))
                .Returns(() => jobConfiguration.ServerUsername);
            mgmtSecretResolver
                .Setup(m => m.Resolve(It.Is<string>(s => s == jobConfiguration.ServerPassword)))
                .Returns(() => jobConfiguration.ServerPassword);
            var mgmt = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU.Management(mgmtSecretResolver.Object);
            var result = mgmt.ProcessJob(jobConfiguration);
            Console.Write(JsonConvert.SerializeObject(result));
            Console.ReadLine();
        }

        public static bool GetItems(IEnumerable<CurrentInventoryItem> items)
        {
            return true;
        }


        public static InventoryJobConfiguration GetInventoryJobConfiguration(string storeType)
        {
            string myFileName = string.Empty;

            if (storeType == "WinIIS")
            {
                myFileName = "WinIISInventory.json";
            }else if (storeType == "WinCert")
            {
                myFileName = "WinCertInventory.json";
            }

            var fileContent = File.ReadAllText(myFileName).Replace("UserNameGoesHere", UserName)
                .Replace("PasswordGoesHere", Password).Replace("StorePathGoesHere", StorePath)
                .Replace("ClientMachineGoesHere", ClientMachine);
            var result =
                JsonConvert.DeserializeObject<InventoryJobConfiguration>(fileContent);
            return result;
        }

        private static ManagementJobConfiguration GetConfigurationFromFile(string fileName)
        {
            var hostNameReplaceString = "\"HostName\": null";
            if (!string.IsNullOrEmpty(HostName))
                hostNameReplaceString = $"\"HostName\": \"{HostName}\"";

            var overWriteReplaceString = "\"Overwrite\": false";
            if (Overwrite.ToUpper() == "TRUE") overWriteReplaceString = "\"Overwrite\": true";

            var replaceDict = new Dictionary<string, string>
            {
                {"UserNameGoesHere", UserName},
                {"PasswordGoesHere", Password},
                {"StorePathGoesHere", StorePath},
                {"AliasGoesHere", CertAlias},
                {"ClientMachineGoesHere", ClientMachine},
                {"WinRmPortGoesHere", WinRmPort},
                {"IPAddressGoesHere", IpAddress},
                {"SiteNameGoesHere", SiteName},
                {"ProtocolGoesHere", Protocol},
                {"SniFlagGoesHere", SniCert},
                {"IISPortGoesHere", Port},
                {"PortGoesHere", Port},
                {"HostNameGoesHere", HostName},
                {"CertificateContentGoesHere", CertificateContent},
                {"\"HostName\": null", hostNameReplaceString},
                {"\"Overwrite\": false", overWriteReplaceString}
            };


            var fileContent = File.ReadAllText($"{fileName}.json");
            foreach (var replaceString in replaceDict)
                fileContent = fileContent.Replace(replaceString.Key, replaceString.Value);

            var result = JsonConvert.DeserializeObject<ManagementJobConfiguration>(fileContent);
            return result;
        }

        public static ManagementJobConfiguration GetManagementJobConfiguration(string fileName)
        {
            return GetConfigurationFromFile(fileName);
        }

        public static ManagementJobConfiguration GetRemoveJobConfiguration()
        {
            return GetConfigurationFromFile("ManagementRemove");
        }
    }
}