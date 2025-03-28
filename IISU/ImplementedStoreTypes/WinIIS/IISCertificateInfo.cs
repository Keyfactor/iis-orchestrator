﻿// Copyright 2025 Keyfactor
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

// 021225 rcp   2.6.0   Cleaned up and verified code

using System;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class IISCertificateInfo
    {
        public string SiteName { get; set; } //
        public string Binding { get; set; } //
        public string IPAddress { get; set; } //
        public string Port { get; set; } //
        public string Protocol { get; set; } //
        public string HostName { get; set; } //
        public string SNI { get; set; } //
        public string Certificate { get; set; } //
        public DateTime ExpiryDate { get; set; } //
        public string Issuer { get; set; } //
        public string Thumbprint { get; set; } //
        public bool HasPrivateKey { get; set; } //
        public string SAN { get; set; } //
        public string ProviderName { get; set; } //
        public string CertificateBase64 { get; set; } //
        public string FriendlyName { get; set; } //

    }
}
