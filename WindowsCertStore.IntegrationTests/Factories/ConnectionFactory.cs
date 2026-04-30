using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsCertStore.IntegrationTests.Factories
{
    internal class ConnectionFactory
    {
        private static (string username, string password) GetCredentials()
        {
            string username = Environment.GetEnvironmentVariable("KEYFACTOR_TEST_USER") ?? string.Empty;
            string password = Environment.GetEnvironmentVariable("KEYFACTOR_TEST_PASSWORD") ?? string.Empty;

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                return (username, password);

            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();

                var vault = new VaultHelper(config);
                return (vault.GetSecret("Username"), vault.GetSecret("Password"));
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
        }

        private static IEnumerable<ClientConnection> LoadConnections()
        {
            var json = File.ReadAllText("servers.json");
            var connections = JsonConvert.DeserializeObject<List<ClientConnection>>(json) ?? new();

            var (username, password) = GetCredentials();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                yield break;

            foreach (var conn in connections)
            {
                if (string.IsNullOrEmpty(conn.Machine) || conn.Machine.StartsWith('{'))
                    continue;

                conn.Username = username;
                conn.PrivateKey = password;
                yield return conn;
            }
        }

        public static IEnumerable<object[]> GetConnection() =>
            LoadConnections().Select(c => new object[] { c });

        public static IEnumerable<object[]> GetIISConnections() =>
            LoadConnections()
                .Where(c => c.StoreType.Equals("WinIIS", StringComparison.OrdinalIgnoreCase))
                .Select(c => new object[] { c });

        public static IEnumerable<object[]> GetSQLConnections() =>
            LoadConnections()
                .Where(c => c.StoreType.Equals("WinSQL", StringComparison.OrdinalIgnoreCase))
                .Select(c => new object[] { c });

        public static IEnumerable<object[]> GetWinCertConnections() =>
            LoadConnections()
                .Where(c => c.StoreType.Equals("WinCert", StringComparison.OrdinalIgnoreCase))
                .Select(c => new object[] { c });
    }
}
