using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IISWithBindings
{
    class StorePath
    {
        public string SiteName { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string HostName { get; set; }
        public string Protocol { get; set; }

        public static StorePath Split(string strStorePath)
        {
            string [] aryStorePath = strStorePath.Split('/');
            if (aryStorePath.Length < 3)
                throw new InvalidStorePathException("Invalid StorePath Format");

            return new StorePath()
            {
                SiteName = aryStorePath[0],
                IP = aryStorePath[1],
                Port = aryStorePath[2],
                HostName = aryStorePath.Length > 3 ? aryStorePath[3] : string.Empty,
                Protocol = "https"
            };
        }

        public string FormatForIIS()
        {
            return $@"{IP}:{Port}:{HostName}";
        }
    }

    public class InvalidStorePathException : ApplicationException
    {
        public InvalidStorePathException(string message) : base(message)
        { }
    }
}
