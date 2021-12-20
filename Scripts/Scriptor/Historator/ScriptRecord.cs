using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor.Historator
{
    public class ScriptRecord
    {
        public string TypeID { get; set; }
        public List<Parameter> Values { get; set; }
    }
}
