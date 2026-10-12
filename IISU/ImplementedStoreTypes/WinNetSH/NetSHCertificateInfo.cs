// Copyright 2026 Keyfactor
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

// Ignore Spelling: Keyfactor NetSH

using System;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinNetSH
{
    public class NetSHCertificateInfo
    {
        public string IPAddress { get; set; }
        public string Port { get; set; }
        public string HostName { get; set; }
        // The AppId currently in effect for this binding, as reported by "netsh http show sslcert" -
        // reflects whatever value was actually used when the binding was created (caller-supplied,
        // reused from a prior binding, or auto-generated), not necessarily what the entry parameter says.
        public string AppId { get; set; }
        public string Certificate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Issuer { get; set; }
        public string Thumbprint { get; set; }
        public bool HasPrivateKey { get; set; }
        public string SAN { get; set; }
        public string ProviderName { get; set; }
        public string CertificateBase64 { get; set; }
        public string FriendlyName { get; set; }
    }
}
