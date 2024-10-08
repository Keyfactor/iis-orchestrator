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

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.IISU
{
    public class ReEnrollment: WinCertJobTypeBase, IReenrollmentJobExtension
    {
        private ILogger _logger;


        public ReEnrollment(IPAMSecretResolver resolver)
        {
            _resolver = resolver;
        }

        public string ExtensionName => "WinIISUReEnrollment";
        
        public JobResult ProcessJob(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReEnrollmentUpdate)
        {

            _logger = LogHandler.GetClassLogger(typeof(ReEnrollment));

            ClientPSCertStoreReEnrollment myReEnrollment = new ClientPSCertStoreReEnrollment(_logger, _resolver);
            return myReEnrollment.PerformReEnrollment(config, submitReEnrollmentUpdate, CertStoreBindingTypeENUM.WinIIS);

        }
    }
}
