using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
 * If using this as a template, be sure to understand that I might want to move this out to its own module at some point.
 * The idea will be to recompile the module at run time in a gui, then reload it into memory.
 */

namespace Scripts.Scripting
{
    [ScriptCollectionName("File Renamers")]
    [ScriptCollectionDescription("Scripts intended to the heavy lifting of renaming files in a directory using a specific format.")]
    public class RenamerScripts : IScriptCollection
    {
        [ScriptRoutine("Rename Files By Order", "Renames the file based on their alphabetical order to the episode order")]
        public static void RenameShowByOrder(
            IScriptContext context,
            [Parameter("Show Name", "The Name of the Show to Use", "This will prefix the name of the show on the files name.", "New Show")]
            string showName,
            [Parameter("Season Number", "The Season of the show that the file will be using", "The Numerical Value of the season. Value must be between 0 <-> 9999", 1)]
            int seasonNumber,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path)
        {
            DirectoryInfo dInfo = new DirectoryInfo(path);

            if (dInfo.Exists)
            {
                int episodeNumber = 1;
                foreach (FileInfo fInfo in dInfo.GetFiles().OrderBy(file => file.Name))
                {
                    string oldName = fInfo.Name;
                    string outputName = string.Format("{0} - S{1}E{2}{3}.", showName, seasonNumber.ToString("00"), episodeNumber.ToString("00"), fInfo.Extension);
                    fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                    Logger.WriteLine(Logger.LogLevel.Event, "Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumber.ToString("00"), oldName, outputName);
                    episodeNumber++;
                }
            }
        }

        [ScriptRoutine("Rename Files By disc in reverse order", "Takes each disc grouping and renames them from the last file to the first for the episode number.")]
        public static void RenameShowByDiscReverseOrder(
            IScriptContext context,
            [Parameter("Show Name", "The Name of the Show to Use", "This will prefix the name of the show on the files name.", "New Show")]
            string showName,
            [Parameter("Season Number", "The Season of the show that the file will be using", "The Numerical Value of the season. Value must be between 0 <-> 9999", 1)]
            int seasonNumber,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path)
        {
            DirectoryInfo dInfo = new DirectoryInfo(path);

            if (dInfo.Exists)
            {
                int episodeNumber = 1;
                int discNumber = 1;
                bool iterate = true;
                FileInfo[] files = dInfo.GetFiles();

                while (iterate)
                {
                    FileInfo[] discFiles = files.Where(file =>
                    {
                        return file.Name.Contains("Disc " + discNumber);
                    }).OrderByDescending(file => file.Name).ToArray();

                    iterate = discFiles.Length > 0;

                    if (!iterate)
                        break;

                    foreach (FileInfo fInfo in discFiles)
                    {
                        string oldName = fInfo.Name;
                        string outputName = string.Format("{0} - S{1}E{2}{3}", showName, seasonNumber.ToString("00"), episodeNumber.ToString("00"), fInfo.Extension);
                        fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                        Logger.WriteLine(Logger.LogLevel.Event, "Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumber.ToString("00"), oldName, outputName);
                        episodeNumber++;
                    }
                    discNumber++;
                }
            }
        }
    }
}
