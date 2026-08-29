using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media.Imaging;
using System.Text;
using System.Threading.Tasks;
using Scripts.Scriptor.Conductor;

namespace GUI.ViewModel
{
    public enum ScriptNodeKind
    {
        Root,
        CollectionsRoot,
        Collection,
        Routine,
        PlaylistsRoot,
        Playlist,
        PlaylistRoutine,
        PlaylistParallelGroup,
        CommandsRoot,
        Command,
    }

    public class ScriptNode : INotifyPropertyChanged
    {
        public Type? ScriptType { get; set; } = null;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ScriptNodeKind Kind { get; set; } = ScriptNodeKind.Root;
        public ScriptCollectionDescriptor? Collection { get; set; }
        public ScriptRoutineDescriptor? Routine { get; set; }
        public PlaylistDefinition? Playlist { get; set; }
        public PlaylistItemDefinition? PlaylistItem { get; set; }
        public CommandDefinition? Command { get; set; }
        public Bitmap? Icon { get; set; }
        public bool HasIcon => Icon != null;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ScriptNode> Children { get; set; } = new ObservableCollection<ScriptNode>();

        public void SetIcon(Bitmap? icon)
        {
            Icon = icon;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
        }
    }
}
