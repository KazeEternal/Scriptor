using System;

namespace GUI.ViewModel
{
    public enum CommandType
    {
        Url,
        Program,
    }

    public sealed class CommandDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CommandType Type { get; set; }
        public string Target { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public string? IconPath { get; set; }
    }
}
