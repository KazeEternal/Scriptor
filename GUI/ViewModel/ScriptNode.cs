using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.ViewModel
{
    public class ScriptNode : INotifyPropertyChanged
    {
        public Type? ScriptType { get; set; } = null;
        public string Name { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ScriptNode> Children { get; set; }
    }
}
