using System.Collections.Generic;

namespace Scripts.Scriptor.Conductor
{
    public sealed class AppSettings
    {
        public string? ScriptsRoot { get; set; }

        public Dictionary<string, string> Values { get; set; } = new();
    }
}
