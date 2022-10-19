using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    [Serializable]
    internal class PowerShellCertException : Exception
    {
        public PowerShellCertException()
        {
        }

        public PowerShellCertException(string message) : base(message)
        {
        }

        public PowerShellCertException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PowerShellCertException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
