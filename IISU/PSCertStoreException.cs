using System;
using System.Runtime.Serialization;

namespace Keyfactor.Extensions.Orchestrator.IISU
{
    [Serializable]
    internal class PsCertStoreException : Exception
    {
        public PsCertStoreException()
        {
        }

        public PsCertStoreException(string message) : base(message)
        {
        }

        public PsCertStoreException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PsCertStoreException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}