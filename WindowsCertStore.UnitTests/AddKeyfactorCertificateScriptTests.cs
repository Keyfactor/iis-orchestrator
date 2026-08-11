using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;
using Microsoft.PowerShell;

namespace WindowsCertStore.UnitTests
{
    /// <summary>
    /// Invokes the Add-KeyfactorCertificate PowerShell function in-process and
    /// verifies it always returns a New-KeyfactorResult-shaped object with a
    /// meaningful Status/Code/Step even in failure paths (bad PFX payload,
    /// missing CSP, etc.).
    ///
    /// These tests exercise the real .ps1 files that ship in the extension so
    /// regressions in the script surface immediately.
    ///
    /// Windows-only because the CSP validation path shells out to certutil.exe.
    /// </summary>
    public class AddKeyfactorCertificateScriptTests
    {
        // A CSP name that will not exist on any real system.
        private const string NonExistentCsp = "___KeyfactorTest_DoesNotExist_CSP___";

        [Fact]
        public void BadBase64_ReturnsError_LoadPfx_Code501()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Extension is Windows-only.
            }

            using var ps = CreateSessionWithScripts();

            ps.AddCommand("Add-KeyfactorCertificate")
              .AddParameter("Base64Cert", "this-is-not-valid-base64!!")
              .AddParameter("StoreName", "My");

            var result = InvokeSingleResult(ps);

            AssertStatus(result, expectedStatus: "Error", expectedStep: "LoadPfx", expectedCode: 501);
            var errorMessage = (string)result.Properties["ErrorMessage"].Value;
            Assert.Contains("PFX", errorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValidBase64ButNotAPfx_ReturnsError_LoadPfx_Code501()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            using var ps = CreateSessionWithScripts();

            // Well-formed Base64, but decoded bytes are not a PFX.
            string junkBase64 = Convert.ToBase64String(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });

            ps.AddCommand("Add-KeyfactorCertificate")
              .AddParameter("Base64Cert", junkBase64)
              .AddParameter("StoreName", "My");

            var result = InvokeSingleResult(ps);

            AssertStatus(result, expectedStatus: "Error", expectedStep: "LoadPfx", expectedCode: 501);
        }

        [Fact]
        public void MissingCsp_ReturnsError_ValidateCsp_Code510_WithAvailableList()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            using var ps = CreateSessionWithScripts();

            // Build a self-signed PFX in-memory so LoadPfx succeeds and we hit ValidateCSP.
            string pfxBase64 = CreateSelfSignedPfxBase64(password: string.Empty, out string _);

            ps.AddCommand("Add-KeyfactorCertificate")
              .AddParameter("Base64Cert", pfxBase64)
              .AddParameter("StoreName", "My")
              .AddParameter("CryptoServiceProvider", NonExistentCsp);

            var result = InvokeSingleResult(ps);

            AssertStatus(result, expectedStatus: "Error", expectedStep: "ValidateCSP", expectedCode: 510);

            var errorMessage = (string)result.Properties["ErrorMessage"].Value;
            Assert.Contains(NonExistentCsp, errorMessage);
            Assert.Contains("Available CSPs:", errorMessage);

            // Verify the C# parser lifts these through to the ResultObject.
            ResultObject parsed = ResultObject.FromPSObject(result);
            Assert.False(parsed.IsSuccess);
            Assert.Equal(510, parsed.Code);
            Assert.Equal("ValidateCSP", parsed.Step);
            Assert.Equal(NonExistentCsp, parsed.Details["RequestedCSP"]);
            Assert.False(string.IsNullOrEmpty(parsed.Thumbprint));
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>
        /// Creates a PowerShell runspace with the Keyfactor.WinCert.Common
        /// module imported. Uses Import-Module against the .psm1 which
        /// explicitly injects exported functions into the runspace's global
        /// scope. This is the same mechanism the JEA endpoint uses in
        /// production, so it exercises the module surface honestly.
        /// </summary>
        private static PowerShell CreateSessionWithScripts()
        {
            string scriptsRoot = PSHelper.FindScriptsDirectory(
                AppDomain.CurrentDomain.BaseDirectory, "PowerShell");

            Assert.False(string.IsNullOrEmpty(scriptsRoot),
                "Could not locate the PowerShell scripts folder in the test output directory.");

            string modulePath = Path.Combine(
                scriptsRoot, "Keyfactor.WinCert.Common", "Keyfactor.WinCert.Common.psm1");

            Assert.True(File.Exists(modulePath),
                $"Keyfactor.WinCert.Common.psm1 was not found at expected path: {modulePath}");

            var iss = InitialSessionState.CreateDefault();
            iss.ExecutionPolicy = ExecutionPolicy.Bypass;
            var runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();

            var ps = PowerShell.Create();
            ps.Runspace = runspace;

            // Import-Module places exported functions into the runspace's
            // global scope so subsequent AddCommand("Add-KeyfactorCertificate")
            // calls resolve correctly.
            ps.AddCommand("Import-Module")
              .AddParameter("Name", modulePath)
              .AddParameter("Force")
              .Invoke();

            if (ps.HadErrors)
            {
                string errors = string.Join("; ", ps.Streams.Error.Select(e => e.ToString()));
                throw new InvalidOperationException($"Failed to import module '{modulePath}': {errors}");
            }

            ps.Commands.Clear();
            ps.Streams.ClearStreams();
            return ps;
        }

        private static PSObject InvokeSingleResult(PowerShell ps)
        {
            var results = ps.Invoke();
            Assert.NotNull(results);
            Assert.True(results.Count >= 1,
                $"Expected the script to return a result object. HadErrors={ps.HadErrors}. " +
                $"Errors: {string.Join("; ", ps.Streams.Error.Select(e => e.ToString()))}");

            return results[0];
        }

        private static void AssertStatus(PSObject result, string expectedStatus, string expectedStep, int expectedCode)
        {
            Assert.NotNull(result);
            Assert.Equal(expectedStatus, (string)result.Properties["Status"].Value);
            Assert.Equal(expectedStep,   (string)result.Properties["Step"].Value);
            Assert.Equal(expectedCode,   (int)result.Properties["Code"].Value);
        }

        /// <summary>
        /// Builds a self-signed cert with an exportable RSA key and returns
        /// the PFX bytes as a Base64 string. Used to exercise script paths
        /// that require a parseable PFX.
        /// </summary>
        private static string CreateSelfSignedPfxBase64(string password, out string thumbprint)
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=KeyfactorUnitTest",
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            thumbprint = cert.Thumbprint;

            byte[] pfxBytes = cert.Export(
                System.Security.Cryptography.X509Certificates.X509ContentType.Pfx,
                password ?? string.Empty);

            return Convert.ToBase64String(pfxBytes);
        }
    }
}
