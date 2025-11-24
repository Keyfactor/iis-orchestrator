using Keyfactor.Orchestrators.Extensions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsCertStore.IntegrationTests.Factories
{
    internal class ConnectionFactory
    {
        // Read the list of IP addresses from an environment variable
        // Get the credential information from Azure Key Vault or another secure location
        public static IEnumerable<object[]> GetConnection()
        {
            // 1. Build configuration to read from appsettings.json
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            var config = builder.Build();

            // 2. Initialize VaultHelper with configuration
            var vault = new VaultHelper(config);

            // 3. Retrieve connection details from configuration
            var json = File.ReadAllText("servers.json");
            var machines = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);

            foreach (var entry in machines)
            {
                string machineName = entry["Machine"];
                string username = vault.GetSecret("Username");
                string password = vault.GetSecret("Password");

                yield return new object[]
                {
                    new ClientConnection
                    {
                        Machine = machineName,
                        Username = username,
                        PrivateKey = password
                    }
                };
            }
        }
    }
}
