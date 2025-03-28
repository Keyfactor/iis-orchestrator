﻿// Copyright 2022 Keyfactor
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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore
{
    public class Certificate
    {
        public string Thumbprint { get; set; }
        public byte[] RawData { get; set; }
        public bool HasPrivateKey { get; set; }
        public string CertificateData => Convert.ToBase64String(RawData);
        public string CryptoServiceProvider { get; set; }
        public string SAN { get; set; }

        public class Utilities
        {
            public static List<T> DeserializeCertificates<T>(string jsonResults)
            {
                if (string.IsNullOrEmpty(jsonResults))
                {
                    // Handle no objects returned
                    return new List<T>();
                }

                // Determine if the JSON is an array or a single object
                if (jsonResults.TrimStart().StartsWith("["))
                {
                    // It's an array, deserialize as list
                    return JsonConvert.DeserializeObject<List<T>>(jsonResults);
                }
                else
                {
                    // It's a single object, wrap it in a list
                    var singleObject = JsonConvert.DeserializeObject<T>(jsonResults);
                    return new List<T> { singleObject };
                }
            }
        }
    }
}