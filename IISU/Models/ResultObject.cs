using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.Orchestrator.WindowsCertStore.Models
{
    public class ResultObject
    {
        public string Status { get; set; }
        public int Code { get; set; }
        public string Step { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Details { get; set; }
    }
}
