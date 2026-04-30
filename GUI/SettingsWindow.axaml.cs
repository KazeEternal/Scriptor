using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GUI
{
    public sealed partial class SettingsWindow : Window
    {
        private TextBox _scriptsPathTextBox = null!;
        private Button _browseScriptsPathButton = null!;
        private Button _saveButton = null!;
        private Button _cancelButton = null!;
        private TextBlock _validationText = null!;
        private readonly Action<string> _onScriptsPathChanged;

        public SettingsWindow(string currentScriptsPath, Action<string> onScriptsPathChanged)
        {
            _onScriptsPathChanged = onScriptsPathChanged ?? throw new ArgumentNullException(nameof(onScriptsPathChanged));
            InitializeComponent();

            _scriptsPathTextBox.Text = currentScriptsPath;
            SelectedScriptsPath = currentScriptsPath;

            _browseScriptsPathButton.Click += BrowseScriptsPathButton_Click;
            _scriptsPathTextBox.LostFocus += ScriptsPathTextBox_LostFocus;
            _scriptsPathTextBox.KeyDown += ScriptsPathTextBox_KeyDown;
            _saveButton.Click += SaveButton_Click;
            _cancelButton.Click += CancelButton_Click;
        }

        public string SelectedScriptsPath { get; private set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _scriptsPathTextBox = this.FindControl<TextBox>("ScriptsPathTextBox") ?? throw new InvalidOperationException("ScriptsPathTextBox not found.");
            _browseScriptsPathButton = this.FindControl<Button>("BrowseScriptsPathButton") ?? throw new InvalidOperationException("BrowseScriptsPathButton not found.");
            _saveButton = this.FindControl<Button>("SaveButton") ?? throw new InvalidOperationException("SaveButton not found.");
            _cancelButton = this.FindControl<Button>("CancelButton") ?? throw new InvalidOperationException("CancelButton not found.");
            _validationText = this.FindControl<TextBlock>("ValidationText") ?? throw new InvalidOperationException("ValidationText not found.");
        }

        private async void BrowseScriptsPathButton_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Scripts Folder",
                Directory = string.IsNullOrWhiteSpace(_scriptsPathTextBox.Text)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : _scriptsPathTextBox.Text
            };

            var selected = await dialog.ShowAsync(this);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _scriptsPathTextBox.Text = selected;
                TryApplyScriptsPath(selected);
            }
        }

        private void ScriptsPathTextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            TryApplyScriptsPath(_scriptsPathTextBox.Text);
        }

        private void ScriptsPathTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                TryApplyScriptsPath(_scriptsPathTextBox.Text);
            }
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (TryApplyScriptsPath(_scriptsPathTextBox.Text))
            {
                Close(true);
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void ShowValidation(string message)
        {
            _validationText.Text = message;
            _validationText.IsVisible = true;
        }

        private void HideValidation()
        {
            _validationText.Text = string.Empty;
            _validationText.IsVisible = false;
        }

        private bool TryApplyScriptsPath(string? path)
        {
            var normalized = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                ShowValidation("Scripts folder path is required.");
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(normalized);
                Directory.CreateDirectory(fullPath);
                HideValidation();

                if (!string.Equals(SelectedScriptsPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedScriptsPath = fullPath;
                    _onScriptsPathChanged(fullPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowValidation($"Invalid scripts folder path: {ex.Message}");
                return false;
            }
        }
    }
}
