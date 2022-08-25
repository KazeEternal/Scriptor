using Scripts.Scriptor;
using Scripts.Scriptor.Historator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GUI.Commands
{
    public class ScriptLoaderCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        private static ScriptLoaderCommand mInstance = null;

        public static ScriptLoaderCommand Instance
        {
            get
            {
                if (mInstance == null)
                {
                    mInstance = new ScriptLoaderCommand();
                }
                return mInstance;
            }
        }

        private ScriptLoaderCommand()
        {

        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            Globals.Archived = Archive.Load("Archive.xml");

            Type type = typeof(IScriptCollection);
            List<Type> typeScriptCollections = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(selected => selected.GetTypes())
                .Where(classType => type.IsAssignableFrom(classType) && type != classType).ToList();

            foreach (Type typeItem in typeScriptCollections)
            {

            }
        }
    }
}
