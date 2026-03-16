using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ScriptNode> Children { get; set; } = new ObservableCollection<ScriptNode>();
    }
}
