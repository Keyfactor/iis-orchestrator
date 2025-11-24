using Keyfactor.Orchestrators.Extensions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.IntegrationTests.Factories
{
    internal class ConfigurationFactory
    {
        public static IEnumerable<InventoryJobConfiguration> GetInventoryConfig()
        {
            yield return new InventoryJobConfiguration
            {
                LastInventory = new List<PreviousInventoryItem>
                {
                    new PreviousInventoryItem
                    {
                    }
                },
                JobCancelled = false,
                ServerError = null,
                RequestStatus = 1,
                ServerUsername = null, //testCase.Username,
                ServerPassword = null, //testCase.Password,
                UseSSL = false,
                JobProperties = null,
                JobTypeId = new Guid("00000000-0000-0000-0000-000000000000"),
                JobId = new Guid("e92f7350-251c-4c0a-9e5d-9b3fdb745ca9"),
                Capability = "CertStores.IISU.Inventory",
                CertificateStoreDetails = new CertificateStore
                {
                    ClientMachine = "", //testCase.Machine,
                    Properties = JsonConvert.SerializeObject(new Dictionary<string, string>
                    {
                        ["spnwithport"]   = "false",
                        ["WinRm Protocol"] = "http",
                        ["WinRm Port"]     = "5985",
                        ["ServerUsername"] = "", // testCase.Username,
                        ["ServerPassword"] = "", // testCase.Password,
                        ["ServerUseSsl"]   = "true"
                    }),
                    StorePath = "My",
                    StorePassword = null,
                    Type = 104
                }
            };
        }

        public static IEnumerable<ManagementJobConfiguration> GetManagementConfig()
        {

            yield return new ManagementJobConfiguration
            {
                LastInventory = new List<PreviousInventoryItem>(),
                JobCancelled = false,
                JobCertificate = new ManagementJobCertificate(),    // Class that is customized during test
                JobProperties = null,                               // Dictionary customized during test
                OperationType = Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Add,
                Overwrite = false,
                ServerError = null,
                JobHistoryId = 12345,
                RequestStatus = 1,
                ServerUsername = "",    // Customize during test
                ServerPassword = "",    // Customize during test
                UseSSL = false,
                JobTypeId = new Guid("00000000-0000-0000-0000-000000000000"),
                JobId = new Guid("e92f7350-251c-4c0a-9e5d-9b3fdb745ca9"),
                Capability = "CertStores.IISU.Management",

                CertificateStoreDetails = new CertificateStore
                {
                    ClientMachine = "",     // Customized during test
                    Properties = "",        // Customized JSON string during test
                    StorePath = "",         // Customized during test
                    StorePassword = null,
                    Type = 104
                }
            };
        }

        public static IEnumerable<object[]> GetInventoryTestData()
        {
            // Define test inputs (machine, username, and password)
            var testCases = new[]
            {
                    new { Machine = "192.168.230.137", Username = "ad\\administrator", Password = "C:\\Users\\bpokorny\\.ssh\\my_rsa" },
                    new { Machine = "192.168.230.137", Username = "ad\\administrator", Password = "C:\\Users\\bpokorny\\.ssh\\my_rsa" }
                };

            foreach (var testCase in testCases)
            {
                yield return new object[]
                {
                        new InventoryJobConfiguration
                        {
                            LastInventory = new List<PreviousInventoryItem>
                            {
                                new PreviousInventoryItem
                                {
                                    Alias = "479D92068614E33B3CB84123AF76F1C40DF4B6F6",
                                    PrivateKeyEntry = true,
                                    Thumbprints = new List<string>
                                    {
                                        "479D92068614E33B3CB84123AF76F1C40DF4B6F6"
                                    }
                                }
                            },
                            JobCancelled = false,
                            ServerError = null,
                            RequestStatus = 1,
                            ServerUsername = testCase.Username,
                            ServerPassword = testCase.Password,
                            UseSSL = false,
                            JobProperties = null,
                            JobTypeId = new Guid("00000000-0000-0000-0000-000000000000"),
                            JobId = new Guid("e92f7350-251c-4c0a-9e5d-9b3fdb745ca9"),
                            Capability = null,
                            CertificateStoreDetails = new CertificateStore
                            {
                                ClientMachine = testCase.Machine,
                                Properties = JsonConvert.SerializeObject(new Dictionary<string, string>
                                {
                                    ["spnwithport"]   = "false",
                                    ["WinRm Protocol"] = "ssh",
                                    ["WinRm Port"]     = "22",
                                    ["ServerUsername"] = testCase.Username,
                                    ["ServerPassword"] = testCase.Password,
                                    ["ServerUseSsl"]   = "true"
                                }),
                                StorePath = "My",
                                StorePassword = null,
                                Type = 104
                            }
                        }
                };
            }
        }
    }
}
