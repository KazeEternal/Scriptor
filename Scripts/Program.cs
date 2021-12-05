using System;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Reflection;
using System.Collections.Generic;

namespace Scripts
{
    class Program
    {
        static void Main(string[] args)
        {
            List<MethodInfo> routines = new List<MethodInfo>();
            string showName = Properties.Script.Default.DefaultShowName;

            Console.WriteLine("---TV SERIES TOOL---");
            Console.WriteLine("String in Parenthesis is default value, press enter without entering anything to use.\n");
            Console.WriteLine("Enter Show Name ({0}): ", showName);
            showName = Console.ReadLine();
            int seasonNumber = -1;

            Console.Clear();
            if (!string.IsNullOrWhiteSpace(showName))
            {
                Console.WriteLine("Using name: {0}", showName);
                Properties.Script.Default.DefaultShowName = showName;
                Properties.Script.Default.Save();
            }
            else
            {
                showName = Properties.Script.Default.DefaultShowName;
                Console.WriteLine("Show name set to '{0}'", showName);
            }

            Console.Clear();
            do
            {
                Console.WriteLine("Enter Season Number:");
                if (int.TryParse(Console.ReadLine(), out seasonNumber))
                {

                }
                else
                {
                    Console.WriteLine("INVALID SEASON NUMBER!!!!");
                }
            } while (seasonNumber < 1);

            Console.Clear();

            Console.WriteLine("Input Path ({0}): ", Properties.Script.Default.InputPath);
            string path = Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(path))
            {
                Properties.Script.Default.InputPath = path;
                Properties.Script.Default.Save();
            }
            else
            {
                path = Properties.Script.Default.InputPath;
            }
            Console.Clear();

            Console.WriteLine("Show Name: {0}", showName);
            Console.WriteLine("Season Number: {0}", seasonNumber.ToString("00"));

            Console.WriteLine("======Select a Routine======");

            int count = 1;
            foreach (MethodInfo item in typeof(Program).GetMethods().Where(method => method.GetCustomAttributes(typeof(ExecutorRoutine), true).Length > 0))
            {
                ExecutorRoutine routine = (ExecutorRoutine)item.GetCustomAttributes(typeof(ExecutorRoutine), true)[0];
                Console.WriteLine("\t({0}) {1}", count++, routine.DisplayName);
                routines.Add(item);
            }

            Console.Write("Script Number: ");
            int decision;
            while (!int.TryParse(Console.ReadLine(), out decision))
            {
                Console.Write("Script Number: ");
            }

            MethodInfo method = routines[decision - 1];
            method.Invoke(null, new object[] { showName, seasonNumber, path });
            //RenameShowByOrder(mShowName, seasonNumber, path);
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
        public static void RenameShowByDiscReverseOrder(string showName, int seasonNumber, string path)
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
                        Console.WriteLine("Episode {0}\n\tOldName: {1}\n\tNewName: {2}", episodeNumber.ToString("00"), oldName, outputName);
                        episodeNumber++;
                    }
                    discNumber++;
                }
            }
        }
    }
}
