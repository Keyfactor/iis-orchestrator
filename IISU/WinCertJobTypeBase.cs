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

using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public abstract class WinCertJobTypeBase
    {
        public IPAMSecretResolver _resolver;
    }

    public enum CertStoreBindingTypeENUM
    {
        None,
        WinIIS,
        WinSQL
    }
}
