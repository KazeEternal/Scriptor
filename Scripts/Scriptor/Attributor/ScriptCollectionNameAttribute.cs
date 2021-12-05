using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor.Attributor
{
    public class ScriptCollectionNameAttribute : Attribute
    {
        public string Name { get; private set; }

        public ScriptCollectionNameAttribute(string name)
        {
            Name = name;
        }
    }
}
