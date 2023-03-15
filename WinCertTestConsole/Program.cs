using Keyfactor.Orchestrators.Extensions;
//using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win;
//using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinIIS;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Moq;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WinCertTestConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Please Select (I)IS cert store or (W)indows cert store");
            var certStoreType = Console.ReadLine().ToUpper().Substring(0,1);

            Console.WriteLine("Please Select (I)nventory or (M)anagement");
            var input = Console.ReadLine();

            switch (input.ToUpper().Substring(0,1))
            {
                case "R":       // Done testing
                    {
                        //using var myRunspace = PSHelper.GetClientPSRunspace("http", "localhost", "5985", false, "kfadmin", "Wh5G2Tc6VBYjSMpC");
                        //myRunspace.Open();
                        //List<Certificate> myCerts = Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities.CertificateStore.GetCertificatesFromStore(myRunspace, "My");

                        //Console.WriteLine($"Number of certs found: {myCerts.Count}");
                        //Console.ReadKey();

                        //List<CurrentInventoryItem> inventory = Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities.CertificateStore.GetIISBoundCertificates(myRunspace,"My");
                        //myRunspace.Close();
                        break;
                    }
                case "I":
                    {

                        //Mock<IPAMSecretResolver> invSecretResolver = new Mock<IPAMSecretResolver>();
                        //invSecretResolver.Setup(m => m.Resolve(It.IsAny<string>())).Returns(() => "LUFRPT0xbXlnVU9OL2d1N05zY0NPbDJPaEtzWDhtVWM9RWUzVTk4YmZPajhTRkRtcTNmTnEzNERHVzdRTWZNWmQxNlBFNXl0UDBnOXVDWGU1bFN6NS9FSklKNFduNGV6dA==");

                        //var inv = new Inventory(invSecretResolver.Object);

                        //var invJobConfig = GetInventoryJobConfiguration();
                        //if (certStoreType == "I")
                        //{
                        //    var inv = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinIIS.Inventory();
                        //    SubmitInventoryUpdate sui = GetItems;
                        //    inv.ProcessJob(invJobConfig, sui);
                        //}
                        //else if(certStoreType=="W")
                        //{
                        //    var inv = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win.Inventory();
                        //    SubmitInventoryUpdate sui = GetItems;
                        //    inv.ProcessJob(invJobConfig, sui);
                        //}
                        break;
                    }
                case "M":
                    {
                        //Console.WriteLine("Select Management (A)dd or (R)emove:");
                        //var mgmtInput = Console.ReadLine();

                        //switch (mgmtInput.ToUpper().Substring(0,1))
                        //{
                        //    case "A":
                        //        {
                        //            Console.WriteLine("Enter Private Key Password ikdj3huXRhtZ, Leave Blank if no Private Key");
                        //            var privateKeyPwd = Console.ReadLine();
                        //            Console.WriteLine("Overwrite? Enter true or false");
                        //            var overWrite = Console.ReadLine();
                        //            Console.WriteLine("Alias Enter Alias Name");
                        //            var alias = Console.ReadLine();
                        //            Console.WriteLine("Trusted Root? Enter true or false");
                        //            var trustedRoot = Console.ReadLine();

                        //            Mock<IPAMSecretResolver> mgmtSecretResolver = new Mock<IPAMSecretResolver>();
                        //            mgmtSecretResolver.Setup(m => m.Resolve(It.IsAny<string>())).Returns(() => "LUFRPT0xbXlnVU9OL2d1N05zY0NPbDJPaEtzWDhtVWM9RWUzVTk4YmZPajhTRkRtcTNmTnEzNERHVzdRTWZNWmQxNlBFNXl0UDBnOXVDWGU1bFN6NS9FSklKNFduNGV6dA==");
                        //            var mgmt = new Keyfactor.Extensions.Orchestrator.WindowsCertStore.Win.Management(mgmtSecretResolver.Object);

                        //            var jobConfiguration = GetJobConfiguration(privateKeyPwd, overWrite, trustedRoot, alias);
                        //            var result = mgmt.ProcessJob(jobConfiguration);

                        //            if (result.Result == Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success)
                        //            {
                        //                Console.WriteLine("Add Success");
                        //            }
                        //            break;
                        //        }
                        //    case "R":
                        //        {
                        //            break;
                        //        }
                        //}
                        break;
                    }
            }
        }

        public static bool GetItems(IEnumerable<CurrentInventoryItem> items)
        {
            return true;
        }

        public static InventoryJobConfiguration GetInventoryJobConfiguration()
        {
            var jobConfigString = "{\"LastInventory\":[],\"CertificateStoreDetails\":{\"ClientMachine\":\"localhost\",\"StorePath\":\"My\",\"StorePassword\":\"\",\"Properties\":\"{\\\"CustField1\\\":\\\"\\\",\\\"ServerUsername\\\":\\\"kfadmin\\\",\\\"ServerPassword\\\":\\\"Wh5G2Tc6VBYjSMpC\\\",\\\"ServerUseSsl\\\":\\\"true\\\"}\",\"Type\":103},\"JobCancelled\":false,\"ServerError\":null,\"JobHistoryId\":3357,\"RequestStatus\":1,\"ServerUsername\":\"kfadmin\",\"ServerPassword\":\"Wh5G2Tc6VBYjSMpC\",\"UseSSL\":true,\"JobProperties\":null,\"JobTypeId\":\"00000000-0000-0000-0000-000000000000\",\"JobId\":\"27eb30f5-f151-4077-acb5-cbc2cc489f7f\",\"Capability\":\"CertStores.Win.Inventory\"}";
            var result = JsonConvert.DeserializeObject<InventoryJobConfiguration>(jobConfigString);

            return result;
        }

        public static ManagementJobConfiguration GetJobConfiguration(string privateKeyPwd, string overWrite, string trustedRoot, string alias)
        {
            //var privateKeyConfig =   $"{{\"LastInventory\":[],\"CertificateStoreDetails\":{{\"ClientMachine\":\"keyfactorpa.eastus2.cloudapp.azure.com\",\"StorePath\":\"public\",\"StorePassword\":null,\"Properties\":\"{{}}\",\"Type\":5109}},\"OperationType\":2,\"Overwrite\":{overWrite},\"JobCertificate\":{{\"Thumbprint\":null,\"Contents\":\"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\",\"Alias\":\"{alias}\",\"PrivateKeyPassword\":\"{privateKeyPwd}\"}},\"JobCancelled\":false,\"ServerError\":null,\"JobHistoryId\":298380,\"RequestStatus\":1,\"ServerUsername\":\"bhill\",\"ServerPassword\":\"LUFRPT0xbXlnVU9OL2d1N05zY0NPbDJPaEtzWDhtVWM9RWUzVTk4YmZPajhTRkRtcTNmTnEzNERHVzdRTWZNWmQxNlBFNXl0UDBnOXVDWGU1bFN6NS9FSklKNFduNGV6dA==\",\"UseSSL\":true,\"JobProperties\":{{\"Trusted Root\":{trustedRoot}}},\"JobTypeId\":\"00000000-0000-0000-0000-000000000000\",\"JobId\":\"d9e6e40b-f9cf-4974-a8c3-822d2c4f394f\",\"Capability\":\"CertStores.PaloAlto.Management\"}}";
            //var noPrivateKeyConfig = $"{{\"LastInventory\":[],\"CertificateStoreDetails\":{{\"ClientMachine\":\"keyfactorpa.eastus2.cloudapp.azure.com\",\"StorePath\":\"public\",\"StorePassword\":null,\"Properties\":\"{{}}\",\"Type\":5109}},\"OperationType\":2,\"Overwrite\":{overWrite},\"JobCertificate\":{{\"Thumbprint\":null,\"Contents\":\"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\",\"Alias\":\"{alias}\",\"PrivateKeyPassword\":null}},\"JobCancelled\":false,\"ServerError\":null,\"JobHistoryId\":298404,\"RequestStatus\":1,\"ServerUsername\":\"bhill\",\"ServerPassword\":\"LUFRPT0xbXlnVU9OL2d1N05zY0NPbDJPaEtzWDhtVWM9RWUzVTk4YmZPajhTRkRtcTNmTnEzNERHVzdRTWZNWmQxNlBFNXl0UDBnOXVDWGU1bFN6NS9FSklKNFduNGV6dA==\",\"UseSSL\":true,\"JobProperties\":{{\"Trusted Root\":{trustedRoot}}},\"JobTypeId\":\"00000000-0000-0000-0000-000000000000\",\"JobId\":\"36a048c2-f051-407d-9f31-a1ec6ab7d913\",\"Capability\":\"CertStores.PaloAlto.Management\"}}";
            var privateKeyConfig =       $"{{\"LastInventory\":[],\"CertificateStoreDetails\":{{\"ClientMachine\":\"localhost\",\"StorePath\":\"My\",\"StorePassword\":null,\"Properties\":\"{{\\\"CustField1\\\":\\\"\\\",\\\"ServerUsername\\\":\\\"kfadmin\\\",\\\"ServerPassword\\\":\\\"Wh5G2Tc6VBYjSMpC\\\",\\\"ServerUseSsl\\\":\\\"true\\\"}}\",\"Type\":103}},\"OperationType\":2,\"Overwrite\":{overWrite},\"JobCertificate\":{{\"Thumbprint\":null,\"Contents\":\"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\",\"Alias\":\"{alias}\",\"PrivateKeyPassword\":\"{privateKeyPwd}\"}},\"JobCancelled\":false,\"ServerError\":null,\"JobHistoryId\":5028,\"RequestStatus\":1,\"ServerUsername\":\"kfadmin\",\"ServerPassword\":\"Wh5G2Tc6VBYjSMpC\",\"UseSSL\":true,\"JobProperties\":{{\"EntryParam1\":null}},\"JobTypeId\":\"00000000-0000-0000-0000-000000000000\",\"JobId\":\"8abad458-5f69-4ca4-b76a-47134649d6d1\",\"Capability\":\"CertStores.Win.Management\"}}";

            //var jobConfigString = privateKeyPwd.Length > 0 ? privateKeyConfig : noPrivateKeyConfig;

            var result = JsonConvert.DeserializeObject<ManagementJobConfiguration>(privateKeyConfig);
            return result;
        }
    }
}
