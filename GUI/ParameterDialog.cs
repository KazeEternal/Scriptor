using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Scripts.Scriptor.Conductor;

namespace GUI
{
    public sealed class ParameterDialog : Window
    {
        private readonly List<(ScriptParameterDescriptor Descriptor, TextBox Input)> _inputs = new();

        public ParameterDialog(IReadOnlyList<ScriptParameterDescriptor> parameters)
        {
            Title = "Script Parameters";
            Width = 520;
            Height = 480;

            var mainPanel = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(12)
            };

            foreach (var parameter in parameters)
            {
                var label = new TextBlock
                {
                    Text = parameter.DisplayName ?? parameter.Name,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                };

                if (!string.IsNullOrWhiteSpace(parameter.Usage))
                {
                    ToolTip.SetTip(label, parameter.Usage);
                }

                var description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? null
                    : new TextBlock { Text = parameter.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

                var input = new TextBox
                {
                    Text = parameter.DefaultValue?.ToString() ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    ToolTip.SetTip(input, parameter.Description);
                }

                mainPanel.Children.Add(label);
                if (description != null)
                {
                    mainPanel.Children.Add(description);
                }
                mainPanel.Children.Add(input);

                _inputs.Add((parameter, input));
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            var okButton = new Button { Content = "Run" };
            okButton.Click += (_, _) =>
            {
                if (TryCollectValues(out var values))
                {
                    Values = values;
                    Close(true);
                }
            };

            var cancelButton = new Button { Content = "Cancel" };
            cancelButton.Click += (_, _) => Close(false);

            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            mainPanel.Children.Add(buttons);

            Content = new ScrollViewer { Content = mainPanel };
        }

        public IReadOnlyList<object?>? Values { get; private set; }

        private bool TryCollectValues(out List<object?> values)
        {
            values = new List<object?>();

            foreach (var (descriptor, input) in _inputs)
            {
                var text = input.Text ?? string.Empty;
                if (!TryParseValue(descriptor.ParameterType, text, out var value))
                {
                    input.BorderBrush = Avalonia.Media.Brushes.Red;
                    return false;
                }

                values.Add(value);
            }

            return true;
        }

        private static bool TryParseValue(Type parameterType, string input, out object? value)
        {
            if (parameterType == typeof(string))
            {
                value = input;
                return true;
            }

            if (parameterType == typeof(int) && int.TryParse(input, out var intValue))
            {
                value = intValue;
                return true;
            }

            if (parameterType == typeof(bool) && bool.TryParse(input, out var boolValue))
            {
                value = boolValue;
                return true;
            }

            if (parameterType.IsEnum && Enum.TryParse(parameterType, input, true, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            value = null;
            return false;
        }
    }
}
