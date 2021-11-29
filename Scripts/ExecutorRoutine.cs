using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts
{
    public class ExecutorRoutine : Attribute
    {
        public string DisplayName { get; private set; }
        public string Description { get; private set; }

        public ExecutorRoutine(string displayName, string description)
        {
            DisplayName = displayName;
            Description = description;
        }
    }
}
