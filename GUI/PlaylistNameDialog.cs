using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GUI
{
    internal sealed class PlaylistNameDialog : Window
    {
        private readonly TextBox _input;

        private PlaylistNameDialog(string title, string value)
        {
            Title = title;
            Width = 420;
            Height = 160;
            CanResize = false;

            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
            _input = new TextBox { Text = value };
            panel.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            var save = new Button { Content = "Save" };
            save.Click += (_, _) =>
            {
                var name = _input.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Close(name);
                }
            };
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += (_, _) => Close(null);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Opened += (_, _) => _input.Focus();
        }

        public static Task<string?> ShowAsync(Window owner, string title, string value)
        {
            return new PlaylistNameDialog(title, value).ShowDialog<string?>(owner);
        }

        public static async Task ShowMessageAsync(Window owner, string message)
        {
            var dialog = new Window
            {
                Title = "Playlist Editor",
                Width = 380,
                Height = 150,
                CanResize = false,
            };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => dialog.Close();
            panel.Children.Add(ok);
            dialog.Content = panel;
            await dialog.ShowDialog(owner);
        }

        public static async Task<bool> ConfirmAsync(Window owner, string message)
        {
            var dialog = new Window
            {
                Title = "Confirm Playlist Deletion",
                Width = 420,
                Height = 160,
                CanResize = false,
            };
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            var delete = new Button { Content = "Delete", Foreground = Brushes.IndianRed };
            delete.Click += (_, _) => dialog.Close(true);
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(delete);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            return await dialog.ShowDialog<bool>(owner);
        }
    }
}
