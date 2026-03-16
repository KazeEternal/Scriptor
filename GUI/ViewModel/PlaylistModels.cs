using System;
using System.Collections.Generic;

namespace GUI.ViewModel
{
    public enum PlaylistItemType
    {
        Routine,
        ParallelGroup,
    }

    public sealed class PlaylistItemDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public PlaylistItemType Type { get; set; } = PlaylistItemType.Routine;
        public string? RoutineId { get; set; }
        public Dictionary<string, string> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PlaylistItemDefinition> Children { get; set; } = new();
    }

    public sealed class PlaylistDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<PlaylistItemDefinition> Items { get; set; } = new();
    }
}
