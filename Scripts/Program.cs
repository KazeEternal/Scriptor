using System;
using System.IO;
using System.Linq;
using System.Configuration;

namespace Scripts
{
    class Program
    {
        static void Main(string[] args)
        {
            string mShowName = Properties.Script.Default.DefaultShowName;

            Console.WriteLine("---TV SERIES TOOL---");
            Console.WriteLine("String in Parenthesis is default value, press enter without entering anything to use.\n");
            Console.WriteLine("Enter Show Name ({0}): ", mShowName);
            String? readShow = Console.ReadLine();
            int seasonNumber = -1;

            Console.Clear();
            if (string.IsNullOrWhiteSpace(readShow))
            {
                Console.WriteLine("Using name: {0}", mShowName);
            }
            else
            {
                mShowName = readShow;
                Console.WriteLine("Show name set to '{0}'", mShowName);
                Properties.Script.Default.DefaultShowName = mShowName;
                Properties.Script.Default.Save();
            }

            Console.Clear();
            do
            {
                Console.WriteLine("Enter Season Number:");
                if(int.TryParse(Console.ReadLine(), out seasonNumber))
                {

                }
                else
                {
                    Console.WriteLine("INVALID SEASON NUMBER!!!!");
                }
            } while (seasonNumber < 1);

            Console.Clear();

            Console.WriteLine("Input Path: ");
            string path = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("Using name: {0}", path);
            }
            else
            {
                mShowName = readShow;
                Console.WriteLine("Show name set to '{0}'", path);
                Properties.Script.Default.InputPath = path;
                Properties.Script.Default.Save();

            }
            Console.Clear();

            Console.WriteLine("Show Name: {0}", mShowName);
            Console.WriteLine("Season Number: {0}", seasonNumber.ToString("00"));

            Console.WriteLine("======Select a Routine======");

            int count = 1;
            foreach(var item  in typeof(Program).GetMethods().Where(method => method.GetCustomAttributes(typeof(ExecutorRoutine), true).Length > 0))
            {
                ExecutorRoutine routine = (ExecutorRoutine)item.GetCustomAttributes(typeof(ExecutorRoutine), true)[0];
                Console.WriteLine("\t({0}) {1}", count++, routine.DisplayName);
            }

            RenameShowByOrder(mShowName, seasonNumber, path);
        }

        [ExecutorRoutine("Rename Files By Order", "Renames the file based on their alphabetical order to the episode order")]
        public static void RenameShowByOrder(string showName, int seasonNumber, string path)
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
                    Console.WriteLine("Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumber.ToString("00"), oldName, outputName);
                    episodeNumber++;
                }
            }
        }

        [ExecutorRoutine("Rename Files By disc in reverse order", "Takes each disc grouping and renames them from the last file to the first for the episode number.")]
        public static void RenameShowByDiscReverseOrder(string showName, int seasonNumber, string path, string discRegEx)
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
                        string outputName = string.Format("{0} - S{1}E{2}{3}.", showName, seasonNumber.ToString("00"), episodeNumber.ToString("00"), fInfo.Extension);
                        fInfo.MoveTo(Path.Combine(dInfo.FullName, outputName));
                        Console.WriteLine("Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumber.ToString("00"), oldName, outputName);
                        episodeNumber++;
                    }
                }
            }
        }
    }
}
