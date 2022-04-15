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
            [Parameter("Episode Starting Number", "The Episode Number to start with.", "The value must be from 0 to N")]
            int episodeStartingNumber,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path)
        {
            DirectoryInfo dInfo = new DirectoryInfo(path);

            if (dInfo.Exists)
            {
                int episodeNumber = episodeStartingNumber;
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

        [ScriptRoutine("Rename Files Cleanly based on S##E##", "Assumes that the season and episode naming convention is already applied and needs to be cleaned up.")]
        public static void RenameShowCleanly(
            IScriptContext context,
            [Parameter("Show Name", "The Name of the Show to Use", "This will prefix the name of the show on the files name.", "New Show")]
            string showName,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path)
        {
            DirectoryInfo dInfo = new DirectoryInfo(path);

            if (dInfo.Exists)
            {
                
                foreach (FileInfo fInfo in dInfo.GetFiles().OrderBy(file => file.Name))
                {
                    string oldName = fInfo.Name;

                    Regex regex = new Regex(@"S\d{1,2}([-+]?E\d{1,2})+");
                    Match match = regex.Match(oldName);

                    if (match.Success)
                    {
                        string outputName = string.Format("{0} - {1}.{2}", showName, match.Value, fInfo.Extension);
                        fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                        Logger.WriteLine(Logger.LogLevel.Event, "Episode {0}\n\tOldName: {1}\n\tNewName: {2}", match.Value.Substring(match.Value.IndexOf("S") + 1), oldName, outputName);
                    }
                    
                }
            }
        }
        [ScriptRoutine("Rename Files By Disc Offset One Ring Order", "Takes the disc number and offsets order by N ")]
        public static void RenameShowByRoundRobinDiscOrder(
            IScriptContext context,
            [Parameter("Show Name", "The Name of the Show to Use", "This will prefix the name of the show on the files name.", "New Show")]
            string showName,
            [Parameter("Season Number", "The Season of the show that the file will be using", "The Numerical Value of the season. Value must be between 0 <-> 9999", 1)]
            int seasonNumber,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path,
            [Parameter("Offset by N", "The number of episodes starting from n to n+1 from")]
            int offset)
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
                    }).ToArray();

                    iterate = discFiles.Length > 0;

                    if (!iterate)
                        break;

                    
                    int currentOffset = discFiles.Length - offset;
                    int episodeNumberOffset = currentOffset + episodeNumber;
                    int startingEpisodeNumber = episodeNumber;
                    foreach (FileInfo fInfo in discFiles)
                    {
                        
                        string oldName = fInfo.Name;
                        string outputName = string.Format("{0} - S{1}E{2}{3}", showName, seasonNumber.ToString("00"), episodeNumberOffset.ToString("00"), fInfo.Extension);
                        fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                        Logger.WriteLine(Logger.LogLevel.Event, "Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumberOffset.ToString("00"), oldName, outputName);
                        currentOffset++;
                        episodeNumberOffset++;
                        if (currentOffset >= discFiles.Length)
                        {
                            episodeNumberOffset = startingEpisodeNumber;
                            currentOffset = 0;
                        }
                        episodeNumber++;
                    }
                    discNumber++;
                }
            }
        }
        [ScriptRoutine("Rename Files that were manual inspected", "All files with the n.1 value for an episode will get renamed to S##EN. This is for manual sorting. Anything else will remain untouched.")]
        public static void RenameShowByTruncatingDecimal(
            IScriptContext context,
            [Parameter("Folder Path", "The folder where the files live.")]
            string path)
        {
            DirectoryInfo dInfo = new DirectoryInfo(path);

            if (dInfo.Exists)
            {
                
                FileInfo[] files = dInfo.GetFiles();

                foreach (FileInfo fInfo in files)
                {
                    Regex regex = new Regex(@"S\d{1,2}([-+]?E\d{1,2})+.1");

                    Match match = regex.Match(fInfo.Name);
                    if (match.Success)
                    {
                        int index = fInfo.Name.IndexOf(match.Value);
                        string prefix = fInfo.Name.Substring(0, index);
                        string postfix = fInfo.Name.Substring(index + match.Value.Length);
                        string oldName = fInfo.Name;

                        string outputName = string.Format("{0}{1}{2}", prefix, match.Value.Substring(0, match.Value.IndexOf('.')), postfix);
                        fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                        Logger.WriteLine(Logger.LogLevel.Event, "\n\tOldName: {0}\n\tNewName: {1}", oldName, outputName);
                    }  
                }
            }
        }
    }
}
