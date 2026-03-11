using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/*
 * If using this as a template, be sure to understand that I might want to move this out to its own module at some point.
 * The idea will be to recompile the module at run time in a gui, then reload it into memory.
 */

namespace Scripts.Scripting
{
    [ScriptCollectionName("Test Scripts")]
    [ScriptCollectionDescription("Scripts for testing hot loading of scripts. This collection is not meant to be used for anything other than testing.")]
    public class HelloWorldScripts : IScriptCollection
    {
        [ScriptRoutine("Hello to a Person", "Says Hello To a Person")]
        public static void HelloWorlder(
            IScriptContext context,
            [Parameter("Person Name", "The Name of the Person to Use", "This will prefix the name of the person on the files name.", "New Person")]
            string personName)
        {
            Logger.WriteLine(Logger.LogLevel.Event, "Hello {0} Test 2", personName);    
        }
    }
}
