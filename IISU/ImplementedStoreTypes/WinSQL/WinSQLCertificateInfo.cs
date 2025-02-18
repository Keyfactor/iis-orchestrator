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

// 021225 rcp   2.6.0   Cleaned up and verified code

// Ignore Spelling: Keyfactor sql

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinSql
{

    public class Parameters
    {
        public string InstanceName { get; set; }
        public object ProviderName { get; set; }
    }

    public class WinSQLCertificateInfo
    {
        public string Certificates { get; set; }
        public string Alias { get; set; }
        public bool PrivateKeyEntry { get; set; }
        public bool UseChainLevel { get; set; }
        public string ItemStatus { get; set; }
        public Parameters Parameters { get; set; }
    }
}
