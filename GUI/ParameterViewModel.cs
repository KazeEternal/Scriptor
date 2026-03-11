using System;

namespace GUI
{
    public sealed class ParameterViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Usage { get; init; } = string.Empty;
    }
}
