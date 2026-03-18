using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scripting
{
    [ScriptCollectionName("Parsers")]
    [ScriptCollectionDescription("Analyzers of file contents and file systems.")]
    public class ParsingScripts : IScriptCollection
    {
        [ScriptRoutine("Get list of files by extension.", "Takes a path a lists all files by extension and puts them in a file.")]
        public static void DumpFilesByExtensionToFile(
           IScriptContext context,
           [Parameter("Folder Path", "The path of the directory to parse over")]
            DirectoryInfo pathFolder,
           [Parameter("Extensions", "The Extension of the files.")]
            string extension,
           [Parameter("Output File Path", "The file to be written too.")]
            string outputFile,
           [Parameter("Invert", "Not that extension", "set to true if you want to not have files with that extension",false)]
            bool invert)
        {
            if (pathFolder == null)
            {
                Console.WriteLine("Path Folder is null or empty string, unable to parse.");
                return;
            }

            DirectoryInfo directoryInfo = pathFolder;

            TextWriter output;
            bool cleanUp = false;
            if (String.IsNullOrEmpty(outputFile))
            {
                output = Console.Out;
            }
            else 
            {
                output = new StreamWriter(File.OpenWrite(outputFile));
                cleanUp = true;
            }

            if (directoryInfo.Exists)
            {
                if (invert)
                {
                    foreach(FileInfo fileInfo in directoryInfo.EnumerateFiles().Where(o => { return o.Extension != "." + extension; }).OrderBy( o => o.Name))
                    {
                        output.WriteLine(fileInfo.Name);
                    }
                }
                else
                {
                    foreach (var fileInfo in directoryInfo.GetFiles("*." + extension))
                    {
                        output.WriteLine(fileInfo.Name);
                    }
                }
            }
            else
            {
                output.WriteLine("Bad File Path");
            }

            if (cleanUp)
            {
                output.Flush();
                output.Close();
            }
            Console.WriteLine("Finished");
        }
    }
}
