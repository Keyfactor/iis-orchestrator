// Copyright 2022 Keyfactor
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

using System;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.PowerShellUtilities
{
    public class Certificate
    {
        public string Thumbprint { get; set; }
        public byte[] RawData { get; set; }
        public bool HasPrivateKey { get; set; }
        public string CertificateData => Convert.ToBase64String(RawData);
    }
}