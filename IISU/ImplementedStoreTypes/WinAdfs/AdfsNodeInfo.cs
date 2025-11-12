// Copyright 2025 Keyfactor
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
// limitations under the License.using Keyfactor.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.ImplementedStoreTypes.WinAdfs
{
    public class AdfsNodeInfo
    {
        public string NodeName { get; set; }
        public string Role { get; set; }
        public bool IsReachable { get; set; }
        public string ServiceStatus { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string SyncStatus { get; set; }
        public string ErrorMessage { get; set; }
    }
}
