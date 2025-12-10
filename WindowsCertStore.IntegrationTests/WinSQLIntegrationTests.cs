using Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Moq;
using Newtonsoft.Json;
using WindowsCertStore.IntegrationTests.Factories;

namespace WindowsCertStore.IntegrationTests
{
    public class WinSQLIntegrationTests
    {
        private static (string thumbprint, string base64Pfx, string pfxPassword) CreateTestCertificate()
        {
            string pfxPassword = CertificateFactory.GeneratePfxPassword();
            var (thumbprint, base64Pfx) = CertificateFactory.CreateSelfSignedCert("test.example.com", pfxPassword);
            return (thumbprint, base64Pfx, pfxPassword);
        }

        private static ManagementJobConfiguration CreateManagementJobConfig(
            ClientConnection connection,
            string thumbprint,
            string base64Pfx,
            string pfxPassword,
            string alias,
            Dictionary<string, object> managementJobProperties,
            string certStorejobProperties,
            Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType operationType,
            bool overwrite)
        {
            var job = ConfigurationFactory.GetManagementConfig().First();
            job.ServerUsername = connection.Username;
            job.ServerPassword = connection.PrivateKey;
            job.OperationType = operationType;
            job.Overwrite = overwrite;
            job.JobCertificate = new ManagementJobCertificate
            {
                Thumbprint = thumbprint,
                Contents = base64Pfx,
                Alias = alias,
                PrivateKeyPassword = pfxPassword,
                ContentsFormat = "PFX"
            };
            job.JobProperties = managementJobProperties;
            job.Capability = "CertStores.WinSql.Management";
            job.CertificateStoreDetails.ClientMachine = connection.Machine;
            job.CertificateStoreDetails.StorePath = "My";
            job.CertificateStoreDetails.Properties = certStorejobProperties;
            return job;
        }

        private static InventoryJobConfiguration CreateInventoryJobConfig(ClientConnection connection, string certStorejobProperties)
        {
            var inventoryJob = ConfigurationFactory.GetInventoryConfig().First();
            inventoryJob.ServerUsername = connection.Username;
            inventoryJob.ServerPassword = connection.PrivateKey;
            inventoryJob.CertificateStoreDetails.ClientMachine = connection.Machine;
            inventoryJob.CertificateStoreDetails.StorePath = "My";
            inventoryJob.CertificateStoreDetails.Properties = certStorejobProperties;
            inventoryJob.Capability = "CertStores.WinSql.Inventory";
            return inventoryJob;
        }

        private static Dictionary<string, object> GetManagementJobProperties() => new()
        {
            ["InstanceName"] = "MSSQLSERVER",
            ["ProviderName"] = "",
            ["SAN"] = ""
        };

        private static string GetCertStoreJobProperties(ClientConnection connection) =>
            JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                ["spnwithport"] = "false",
                ["WinRm Protocol"] = "http",
                ["WinRm Port"] = "5985",
                ["ServerUsername"] = connection.Username,
                ["ServerPassword"] = connection.PrivateKey,
                ["ServerUseSsl"] = "false",
                ["RestartService"] = "false"
            });

        private static (bool found, string? alias) FindAliasByThumbprint(IEnumerable<CurrentInventoryItem> inventory, string thumbprint)
        {
            var matchedItem = inventory
                .FirstOrDefault(item => !string.IsNullOrEmpty(item.Alias) &&
                                       item.Alias.Split(':')[0].Equals(thumbprint, StringComparison.OrdinalIgnoreCase));
            return (matchedItem != null, matchedItem?.Alias);
        }

        private static void AssertJobResult(Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult result, string? failureMessage)
        {
            switch (result)
            {
                case Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Failure:
                    Assert.Fail(failureMessage);
                    break;
                case Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success:
                case Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Warning:
                    Assert.True(true);
                    break;
                default:
                    Assert.Fail("Unexpected job result status.");
                    break;
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionFactory.GetConnection), MemberType = typeof(ConnectionFactory))]
        public void WinSql_Management_Add_Inventory_Remove_EndToEnd_Test(ClientConnection connection)
        {
            var (thumbprint, base64Pfx, pfxPassword) = CreateTestCertificate();

            var secretResolver = new Mock<IPAMSecretResolver>();
            secretResolver.Setup(m => m.Resolve(It.IsAny<string>())).Returns((string s) => s);

            var managementJobProperties = GetManagementJobProperties();
            var certStorejobProperties = GetCertStoreJobProperties(connection);

            // Add certificate
            var addJob = CreateManagementJobConfig(
                connection, thumbprint, base64Pfx, pfxPassword, "Test Cert",
                managementJobProperties, certStorejobProperties,
                Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Add, true);

            var management = new Management(secretResolver.Object);
            var result = management.ProcessJob(addJob);
            AssertJobResult(result.Result, result.FailureMessage);

            // Inventory
            var inventoryJob = CreateInventoryJobConfig(connection, certStorejobProperties);
            var inventory = new Inventory(secretResolver.Object);
            IEnumerable<CurrentInventoryItem> returnedInventory = new List<CurrentInventoryItem>();
            SubmitInventoryUpdate submitInventoryUpdate = items =>
            {
                returnedInventory = items;
                return true;
            };
            result = inventory.ProcessJob(inventoryJob, submitInventoryUpdate);
            AssertJobResult(result.Result, result.FailureMessage);

            var (thumbprintFound, returnedAlias) = FindAliasByThumbprint(returnedInventory, thumbprint);
            Assert.True(thumbprintFound, $"The inventory did not return the expected certificate with thumbprint: {thumbprint}");

            // Remove certificate
            var removeJob = CreateManagementJobConfig(
                connection, null, "", "", returnedAlias ?? "",
                managementJobProperties, certStorejobProperties,
                Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Remove, false);

            result = management.ProcessJob(removeJob);
            Assert.NotNull(result);
            AssertJobResult(result.Result, result.FailureMessage);
        }

        [Theory]
        [MemberData(nameof(ConnectionFactory.GetConnection), MemberType = typeof(ConnectionFactory))]
        public void WinCert_Management_Add_Inventory_Renewal_Inventory_Remove_EndToEnd_Test(ClientConnection connection)
        {
            var (thumbprint, base64Pfx, pfxPassword) = CreateTestCertificate();
            var secretResolver = new Mock<IPAMSecretResolver>();
            secretResolver.Setup(m => m.Resolve(It.IsAny<string>())).Returns((string s) => s);

            var managementJobProperties = GetManagementJobProperties();
            var certStorejobProperties = GetCertStoreJobProperties(connection);

            // Add certificate
            var addJob = CreateManagementJobConfig(
                connection, "", base64Pfx, pfxPassword, "",
                managementJobProperties, certStorejobProperties,
                Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Add, true);

            var management = new Management(secretResolver.Object);
            var result = management.ProcessJob(addJob);
            AssertJobResult(result.Result, result.FailureMessage);

            // Inventory
            var inventoryJob = CreateInventoryJobConfig(connection, certStorejobProperties);
            var inventory = new Inventory(secretResolver.Object);
            IEnumerable<CurrentInventoryItem> returnedInventory = new List<CurrentInventoryItem>();
            SubmitInventoryUpdate submitInventoryUpdate = items =>
            {
                returnedInventory = items;
                return true;
            };
            result = inventory.ProcessJob(inventoryJob, submitInventoryUpdate);
            AssertJobResult(result.Result, result.FailureMessage);

            var (thumbprintFound, returnedAlias) = FindAliasByThumbprint(returnedInventory, thumbprint);
            Assert.True(thumbprintFound, $"The inventory did not return the expected certificate with thumbprint: {thumbprint}");

            // Renew certificate
            var (renewalThumbprint, renewalBase64Pfx, renewalPfxPassword) = CreateTestCertificate();
            var renewalJob = CreateManagementJobConfig(
                connection, thumbprint, renewalBase64Pfx, renewalPfxPassword, "",
                managementJobProperties, certStorejobProperties,
                Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Add, true);

            result = management.ProcessJob(renewalJob);
            AssertJobResult(result.Result, result.FailureMessage);

            // Inventory after renewal
            inventoryJob = CreateInventoryJobConfig(connection, certStorejobProperties);
            returnedInventory = new List<CurrentInventoryItem>();
            submitInventoryUpdate = items =>
            {
                returnedInventory = items;
                return true;
            };
            result = inventory.ProcessJob(inventoryJob, submitInventoryUpdate);
            AssertJobResult(result.Result, result.FailureMessage);

            var (renewalThumbprintFound, renewalReturnedAlias) = FindAliasByThumbprint(returnedInventory, renewalThumbprint);
            Assert.True(renewalThumbprintFound, $"The inventory returned the expected certificate with thumbprint: {renewalThumbprint}");

            // Remove renewed certificate
            var removeJob = CreateManagementJobConfig(
                connection, null, "", "", renewalReturnedAlias ?? "",
                managementJobProperties, certStorejobProperties,
                Keyfactor.Orchestrators.Common.Enums.CertStoreOperationType.Remove, false);

            result = management.ProcessJob(removeJob);
            Assert.NotNull(result);
            AssertJobResult(result.Result, result.FailureMessage);
        }
    }
}
