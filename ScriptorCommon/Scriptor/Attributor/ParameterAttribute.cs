using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor.Attributor
{
    public class ParameterAttribute : Attribute
    {
        public string Name { get; set; }    
        public object Default { get; set; }
        public string Description { get; set; } 
        public string Usage { get; set; }

        public ParameterAttribute(string name = "", string description = "", string usage = "", object defaultValue = null)
        {
            Name = name;
            Description = description;
            Usage = usage;
            Default = defaultValue;
        }
    }
}
