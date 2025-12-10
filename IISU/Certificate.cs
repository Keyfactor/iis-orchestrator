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

// Ignore Spelling: Keyfactor

using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

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

            public static string WriteCertificateToTempPfx(string certificateContents)
            {
                if (string.IsNullOrWhiteSpace(certificateContents))
                    throw new ArgumentException("Certificate contents cannot be null or empty.", nameof(certificateContents));

                try
                {
                    // Decode the Base64 string into bytes
                    byte[] certBytes = Convert.FromBase64String(certificateContents);

                    // Create a unique temporary directory
                    string tempDirectory = Path.Combine(Path.GetTempPath(), "CertTemp");
                    Directory.CreateDirectory(tempDirectory);

                    // Create a unique filename
                    string fileName = $"cert_{Guid.NewGuid():N}.pfx";
                    string filePath = Path.Combine(tempDirectory, fileName);

                    // Write the bytes to the .pfx file
                    File.WriteAllBytes(filePath, certBytes);

                    // Return the path to the newly created file
                    return filePath;
                }
                catch (FormatException)
                {
                    throw new InvalidDataException("The provided certificate contents are not a valid Base64 string.");
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to write certificate to temp PFX file: {ex.Message}", ex);
                }
            }

            public static void CleanupTempCertificate(string pfxFilePath)
            {
                ILogger logger = LogHandler.GetClassLogger<Certificate>();

                if (string.IsNullOrWhiteSpace(pfxFilePath))
                    return;

                try
                {
                    if (File.Exists(pfxFilePath))
                    {
                        File.Delete(pfxFilePath);
                    }

                    string? parentDir = Path.GetDirectoryName(pfxFilePath);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        // Delete the directory if it's empty
                        if (Directory.GetFiles(parentDir).Length == 0 &&
                            Directory.GetDirectories(parentDir).Length == 0)
                        {
                            Directory.Delete(parentDir);
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    logger.LogWarning($"Warning: Could not delete temporary file or folder: {ioEx.Message}");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    logger.LogWarning($"Warning: Access denied when cleaning up temp file: {uaEx.Message}");
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Warning: Unexpected error during cleanup: {ex.Message}");
                }
            }
        }
    }
}