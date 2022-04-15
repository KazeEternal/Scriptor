using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor.Attributor
{
    public class ScriptRoutineAttribute : Attribute
    {
        public string Name { get; private set; } = null;
        public string Description { get; private set; } = null;

        public ScriptRoutineAttribute(string displayName, string displayDescription)
        {
            Name = displayName;
            Description = displayDescription;
        }

        public ScriptRoutineAttribute()
        {

        }
    }
}
