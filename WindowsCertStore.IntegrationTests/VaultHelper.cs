using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace WindowsCertStore.IntegrationTests
{
    internal class VaultHelper
    {
        private readonly SecretClient _secretClient;

        public VaultHelper(IConfiguration configuration)
        {
            string vaultUri = configuration["KeyVault:Uri"];
            if (string.IsNullOrWhiteSpace(vaultUri))
                throw new InvalidOperationException("Key Vault URI not found in configuration.");

            _secretClient = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
        }

        public string GetSecret(string name)
        {
            KeyVaultSecret secret = _secretClient.GetSecret(name);
            return secret.Value;
        }
    }
}
