using System;
using System.Runtime.Serialization;

namespace Keyfactor.Extensions.Orchestrator.IISWithBinding
{
    [Serializable]
    internal class PSCertStoreException : Exception
    {
        public PSCertStoreException()
        {
        }

        public PSCertStoreException(string message) : base(message)
        {
        }

        public PSCertStoreException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PSCertStoreException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}