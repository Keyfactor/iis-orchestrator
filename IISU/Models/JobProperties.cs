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

using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    internal class JobProperties
    {

        public JobProperties()
        {
        }

        [JsonProperty("spnwithport")]
        [DefaultValue(false)]
        public bool SpnPortFlag { get; set; }

        [JsonProperty("WinRm Protocol")]
        [DefaultValue("http")]
        public string WinRmProtocol { get; set; }

        [JsonProperty("WinRm Port")]
        [DefaultValue("5985")]
        public string WinRmPort { get; set; }

        [JsonProperty("ServerUsername")]
        public string ServerUsername { get; set; }

        [JsonProperty("ServerUseSsl")]
        [DefaultValue(true)]
        public bool ServerUseSsl { get; set; }

        [JsonProperty("sniflag")]
        [DefaultValue(SniFlag.None)]
        public SniFlag SniFlag { get; set; }

        [JsonProperty("RestartService")]
        [DefaultValue(true)]
        public bool RestartService { get; set; }
    }

    internal enum SniFlag
    {
        None = 0,
        Sni = 1,
        NoneCentral = 2,
        SniCentral = 3
    }
}