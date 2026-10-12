using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models;

namespace WindowsCertStore.UnitTests
{
    public class ResultObjectTests
    {
        [Fact]
        public void FromPSObject_Null_ReturnsErrorWithMessage()
        {
            ResultObject result = ResultObject.FromPSObject(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultObject.StatusError, result.Status);
            Assert.Equal(-1, result.Code);
            Assert.Equal("PowerShell returned a null result object.", result.ErrorMessage);
            Assert.NotNull(result.Details);
            Assert.Empty(result.Details);
        }

        [Fact]
        public void FromPSObject_SuccessWithThumbprintInDetails_Populates()
        {
            PSObject ps = BuildPSObject(
                status: "Success",
                code: 0,
                step: "ImportCertificate",
                message: "Certificate '1234ABCD' added.",
                errorMessage: string.Empty,
                details: new Hashtable { { "Thumbprint", "1234ABCD" } });

            ResultObject result = ResultObject.FromPSObject(ps);

            Assert.True(result.IsSuccess);
            Assert.Equal("Success", result.Status);
            Assert.Equal(0, result.Code);
            Assert.Equal("ImportCertificate", result.Step);
            Assert.Equal("Certificate '1234ABCD' added.", result.Message);
            Assert.Equal("1234ABCD", result.Thumbprint);
        }

        [Fact]
        public void FromPSObject_CspNotFoundError_ExposesAvailableCsps()
        {
            var available = new object[] { "Microsoft Software Key Storage Provider", "Microsoft Platform Crypto Provider" };

            PSObject ps = BuildPSObject(
                status: "Error",
                code: 510,
                step: "ValidateCSP",
                message: string.Empty,
                errorMessage: "The requested Crypto Service Provider 'FooCSP' was not found on the target system. Available CSPs: Microsoft Software Key Storage Provider, Microsoft Platform Crypto Provider",
                details: new Hashtable
                {
                    { "RequestedCSP",  "FooCSP" },
                    { "AvailableCSPs", available },
                    { "Thumbprint",    "ABCDEF12" }
                });

            ResultObject result = ResultObject.FromPSObject(ps);

            Assert.False(result.IsSuccess);
            Assert.Equal("Error", result.Status);
            Assert.Equal(510, result.Code);
            Assert.Equal("ValidateCSP", result.Step);
            Assert.Contains("FooCSP", result.ErrorMessage);
            Assert.Contains("Microsoft Software Key Storage Provider", result.ErrorMessage);

            Assert.Equal("FooCSP", result.Details["RequestedCSP"]);
            Assert.Same(available, result.Details["AvailableCSPs"]);
            Assert.Equal("ABCDEF12", result.Thumbprint);
        }

        [Fact]
        public void FromPSObject_CodeAsString_ParsesToInt()
        {
            PSObject ps = BuildPSObject(
                status: "Error",
                codeRaw: "520",
                step: "CertUtilImport",
                message: string.Empty,
                errorMessage: "certutil exited with code 2148073494",
                details: null);

            ResultObject result = ResultObject.FromPSObject(ps);

            Assert.Equal(520, result.Code);
            Assert.Equal("CertUtilImport", result.Step);
        }

        [Fact]
        public void FromPSObject_MissingProperties_UsesDefaults()
        {
            var ps = new PSObject();
            ps.Properties.Add(new PSNoteProperty("Status", "Error"));

            ResultObject result = ResultObject.FromPSObject(ps);

            Assert.False(result.IsSuccess);
            Assert.Equal("Error", result.Status);
            Assert.Equal(-1, result.Code);
            Assert.Equal(string.Empty, result.Step);
            Assert.Equal(string.Empty, result.Message);
            Assert.Equal(string.Empty, result.ErrorMessage);
            Assert.Empty(result.Details);
            Assert.Equal(string.Empty, result.Thumbprint);
        }

        [Fact]
        public void IsSuccess_CaseInsensitive()
        {
            var result = new ResultObject { Status = "success" };
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Thumbprint_MissingDetail_ReturnsEmpty()
        {
            var result = new ResultObject
            {
                Status = "Success",
                Details = new Dictionary<string, object>()
            };

            Assert.Equal(string.Empty, result.Thumbprint);
        }

        [Fact]
        public void FromPSResults_NullCollection_ReturnsCatchAllError()
        {
            ResultObject result = ResultObject.FromPSResults(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultObject.StatusError, result.Status);
            Assert.Equal("CatchAll", result.Step);
            Assert.Contains("no results", result.ErrorMessage);
        }

        [Fact]
        public void FromPSResults_EmptyCollection_ReturnsCatchAllError()
        {
            ResultObject result = ResultObject.FromPSResults(new Collection<PSObject>());

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultObject.StatusError, result.Status);
            Assert.Equal("CatchAll", result.Step);
        }

        [Fact]
        public void FromPSResults_FirstItemMapped()
        {
            var collection = new Collection<PSObject>
            {
                BuildPSObject(
                    status: "Success",
                    code: 0,
                    step: "ImportCertificate",
                    message: "ok",
                    errorMessage: string.Empty,
                    details: new Hashtable { { "Thumbprint", "AAAABBBB" } })
            };

            ResultObject result = ResultObject.FromPSResults(collection);

            Assert.True(result.IsSuccess);
            Assert.Equal("AAAABBBB", result.Thumbprint);
        }

        private static PSObject BuildPSObject(
            string status,
            int code,
            string step,
            string message,
            string errorMessage,
            Hashtable? details)
        {
            var ps = new PSObject();
            ps.Properties.Add(new PSNoteProperty("Status", status));
            ps.Properties.Add(new PSNoteProperty("Code", code));
            ps.Properties.Add(new PSNoteProperty("Step", step));
            ps.Properties.Add(new PSNoteProperty("Message", message));
            ps.Properties.Add(new PSNoteProperty("ErrorMessage", errorMessage));
            ps.Properties.Add(new PSNoteProperty("Details", details));
            return ps;
        }

        private static PSObject BuildPSObject(
            string status,
            object codeRaw,
            string step,
            string message,
            string errorMessage,
            Hashtable? details)
        {
            var ps = new PSObject();
            ps.Properties.Add(new PSNoteProperty("Status", status));
            ps.Properties.Add(new PSNoteProperty("Code", codeRaw));
            ps.Properties.Add(new PSNoteProperty("Step", step));
            ps.Properties.Add(new PSNoteProperty("Message", message));
            ps.Properties.Add(new PSNoteProperty("ErrorMessage", errorMessage));
            ps.Properties.Add(new PSNoteProperty("Details", details));
            return ps;
        }
    }
}
