// Copyright 2023 Keyfactor
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

using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class ManagementJobLogger : IManagementJobLogger, IManagementCertStoreDetails
    {
        public bool JobCancelled { get; set; }
        public ServerFault ServerError { get; set; } = new ServerFault();
        public long JobHistoryID { get; set; }
        public int RequestStatus { get; set; }
        public string ServerUserName { get; set; }
        public string ServerPassword { get; set; }
        public JobProperties JobConfigurationProperties { get; set; } = new JobProperties();
        public bool UseSSL { get; set; }
        public Guid JobTypeID { get; set; }
        public Guid JobID { get; set; }
        public string Capability { get; set; }

        public IEnumerable<PreviousInventoryItem> LastInventory { get; set; } = new List<PreviousInventoryItem>();

        public CertificateStoreDetailsDTO CertificateStoreDetails { get; set; } = new CertificateStoreDetailsDTO();
        public CertificateStoreDetailPropertiesDTO CertificateStoreDetailProperties { get; set; } = new CertificateStoreDetailPropertiesDTO();

        public CertStoreOperationType OperationType { get; set; }
        public bool Overwrite { get; set; }

        public JobCertificateDTO JobCertificateProperties { get; set; } = new JobCertificateDTO();
    }
}
