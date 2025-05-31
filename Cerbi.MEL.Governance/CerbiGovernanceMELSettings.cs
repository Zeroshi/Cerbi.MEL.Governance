using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerbi
{
    public class CerbiGovernanceMELSettings
    {
        public string Profile { get; set; } = "default";
        public string ConfigPath { get; set; } = "cerbi_governance.json";
        public bool Enabled { get; set; } = true;
    }
}

