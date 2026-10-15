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

// Ignore Spelling: Keyfactor NetSH Sslcert Ipport Hostnameport

using System;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.WinNetSH
{
    // Identifies a single "netsh http sslcert" binding. There is no "site" concept the way there is
    // for WinIIS, so the identity is just the ipport (or hostnameport, when HostName is set) key -
    // AppId is required by netsh to *add* a binding but is not part of the binding's identity and is
    // not needed to *delete* one, so it is intentionally not encoded in the Alias (see AppId behavior
    // described in docsource/winnetsh.md: supplied / reused-from-existing-binding / auto-generated).
    public class NetSHBindingInfo
    {
        public string IPAddress { get; set; }
        public string Port { get; set; }
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public string? HostName { get; set; }
        public string? AppId { get; set; }
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public string Thumbprint { get; private set; }

        public NetSHBindingInfo()
        {

        }

        public NetSHBindingInfo(Dictionary<string, object> bindingInfo)
        {
            try
            {
                IPAddress = bindingInfo["IPAddress"].ToString();
                Port = bindingInfo["Port"].ToString();
                HostName = bindingInfo.ContainsKey("HostName") ? bindingInfo["HostName"]?.ToString() : null;
                // AppId is optional - a missing/blank value means "resolve automatically" (see class summary).
                AppId = bindingInfo.ContainsKey("AppId") ? bindingInfo["AppId"]?.ToString() : null;
            }
            catch (KeyNotFoundException ex)
            {
                throw new ArgumentException($"An Entry Parameter was missing. Please check the Cert Store Type Definition, note that entry parameters are case sensitive. Message: {ex.Message}");
            }
        }

        public static NetSHBindingInfo ParseAliasBindingString(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("Alias cannot be null or empty.", nameof(alias));

            var parts = alias.Split(':');
            if (parts.Length < 3 || parts.Length > 4)
                throw new FormatException("Alias must be in the format of Thumbprint:IPAddress:Port[:Hostname]");

            return new NetSHBindingInfo
            {
                Thumbprint = parts[0],
                IPAddress = parts[1],
                Port = parts[2],
                HostName = parts.Length == 4 ? parts[3] : null
            };
        }
    }
}
