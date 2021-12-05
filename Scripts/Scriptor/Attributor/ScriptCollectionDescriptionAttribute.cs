using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor.Attributor
{
    public class ScriptCollectionDescriptionAttribute : Attribute
    {
        public string Description { get; private set; }
        public ScriptCollectionDescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
