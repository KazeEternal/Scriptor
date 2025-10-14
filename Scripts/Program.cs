#define NEW_CODE
using System;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Reflection;
using System.Collections.Generic;
using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System.Text.RegularExpressions;
using Scripts.Scriptor.Historator;

namespace Scripts
{
    class Program
    {
#if NEW_CODE
        private static IScriptContext _ScriptContext;
        public static void Main(string[] args)
        {
            Logger.Event += Logger_Event;
            Logger.Warning += Logger_Warning;
            Logger.Error += Logger_Error;

            Archive archive = Archive.Load("Archive.xml");

            bool isRunning = true;
            while (isRunning)
            {
                Type type = typeof(IScriptCollection);
                List<Type> typeScriptCollections = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(selected => selected.GetTypes())
                    .Where(classType => type.IsAssignableFrom(classType) && type != classType).ToList();

                int indexCounter = 0;
                Console.WriteLine("Please Select the type of Operation you are executing:");

                foreach (Type collectionType in typeScriptCollections)
                {
                    indexCounter++;
                    String name = (collectionType.GetCustomAttribute(typeof(ScriptCollectionNameAttribute)) as ScriptCollectionNameAttribute)?.Name;
                    String Description = (collectionType.GetCustomAttribute(typeof(ScriptCollectionDescriptionAttribute)) as ScriptCollectionDescriptionAttribute)?.Description;

                    Console.WriteLine("{0}) {1} - {2}\n", indexCounter, name != null ? name : collectionType.Name, Description != null ? Description : String.Empty);
                }

                int selection;
                while (!ReaderSelection(out selection) && (selection - 1) < typeScriptCollections.Count && selection > 0) ;

                Console.Clear();

                _ScriptContext = new IScriptContext();
                HandleScriptCollection(typeScriptCollections[selection - 1]);

                Console.Write("Completed Running {0}: ", _ScriptContext.Name);
                Console.ForegroundColor = _ScriptContext.IsSuccess ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(_ScriptContext.IsSuccess ? "[SUCCESS]" : "[FAILED]");
                Console.ForegroundColor = ConsoleColor.White;

                while (true)
                {
                    Console.WriteLine("Start Over (Y/n)? ");
                    string result = Console.ReadLine();

                    if(string.IsNullOrEmpty(result) || result.ToLower() == "y")
                    {
                        Console.Clear();
                        break;
                    }
                    else if(result.ToLower() == "n")
                    {
                        isRunning = false;
                        break;
                    }
                    
                }
            }
        }

        private static void Logger_Error(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(format, args);
            Console.ResetColor();
        }

        private static void Logger_Warning(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(format, args);
            Console.ResetColor();
        }

        private static void Logger_Event(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        private static void HandleScriptCollection(Type scriptCollectionType)
        {
            //We Only care about the default constructor. If we need more than that we'll have to expand functionality. 
            //Anything else should be handled through ScriptContext.
            ConstructorInfo ctor = scriptCollectionType.GetConstructor(new Type[] { });

            //We now have an instance of the type that we can pass into our method calls. 
            //Should allow the script writer to maintain values they deem valuable.
            //Not sure if this is a wise move given ScriptContext.
            object instance = ctor.Invoke(null); 

            //We now want to grab the methods within based on the ScriptRoutineAttribute. 
            //The provide them to the user as scripts they can run from the script collection that was passed in.
            MethodInfo[] methods = scriptCollectionType.GetMethods().Where(method => method.GetCustomAttributes(typeof(ScriptRoutineAttribute), true).Length > 0).ToArray();
            int indexCounter = 0;
            Console.WriteLine("Please Select a Script to Run from the Colleciton:");
            foreach (MethodInfo method in methods)
            {
                indexCounter++;
                ScriptRoutineAttribute routine = (ScriptRoutineAttribute)method.GetCustomAttributes(typeof(ScriptRoutineAttribute), true)[0];
                Console.WriteLine("\t{0}) {1}", indexCounter, routine.Name != null ? routine.Name : method.Name);
                Console.WriteLine("\t     {0}", routine.Description != null ? InsertNthAsString(routine.Description, "\n\t") : String.Empty);
            }

            int selection;
            while (!ReaderSelection(out selection) && (selection - 1) < methods.Length && selection > 0) ;

            MethodInfo methodToRun = methods[selection - 1];
            ScriptRoutineAttribute routineToRun = (ScriptRoutineAttribute)methodToRun.GetCustomAttributes(typeof(ScriptRoutineAttribute), true)[0];

            Console.Clear();
            Console.WriteLine("---------Setup to Run {0}----------", routineToRun.Name != null ? routineToRun.Name : methodToRun.Name);
            List<object> args = new List<object>();
            args.Add(_ScriptContext);
            foreach(ParameterInfo pInfo in methodToRun.GetParameters())
            {
                if (pInfo.ParameterType == typeof(IScriptContext))
                    continue;

                Attribute attribute = pInfo.GetCustomAttribute(typeof(ParameterAttribute));
                ParameterAttribute pAt = attribute as ParameterAttribute;

                string lastValue = Archive.GetArchive().Retrieve(methodToRun.DeclaringType, methodToRun.Name, pInfo.Name);

                object toAdd;
                while (!ParseInput(pInfo.ParameterType,
                        string.Format("-->{0}{1} : ", pAt?.Name != null ? pAt?.Name : pInfo.Name, lastValue != null ? $"({lastValue})" : string.Empty),
                                    out toAdd, lastValue))
                {
                    Console.WriteLine("Incorrect format type, looking for {0}", pInfo.ParameterType.FullName);
                }
                Archive.GetArchive().Change(methodToRun.DeclaringType, methodToRun.Name, pInfo.Name,toAdd);
                args.Add(toAdd);
            }

            _ScriptContext.Name = routineToRun.Name;
            Archive.GetArchive().Commit();
            methodToRun.Invoke(instance, args.ToArray());
        }

        private static bool ParseInput(Type parameterType, string itemToRequest, out object toAdd, string defaultValue = "")
        {
            Console.Write(itemToRequest);
            String value = Console.ReadLine();
            
            if(String.IsNullOrWhiteSpace(value))
            {
                value = defaultValue;
            }

            bool isSuccess = false;
            toAdd = null;
            if (parameterType == typeof(string) ||
                parameterType == typeof(String))
            {
                toAdd = value;
                isSuccess = true;
            }
            else if (parameterType == typeof(int))
            {
                int output;
                if (int.TryParse(value, out output))
                {
                    toAdd = output;
                    isSuccess = true;
                }
            }
            else if (parameterType == typeof(bool))
            {
                bool output;
                if(bool.TryParse(value, out output))
                {
                    toAdd = output;
                    isSuccess = true;
                }
            }
            
            return isSuccess;
        }

        public static bool ReaderSelection(out int selection)
        {
            Console.Write("Enter Selection: ");
            return int.TryParse(Console.ReadLine(), out selection);
        }

        public static string InsertNthAsString(string input, string insert)
        {
            const int maxLineLength = 85;
            //input = Regex.Replace(input, @"\s+\n", "\n", RegexOptions.Multiline);

            return Regex.Replace(input, @"(?s).{0," + maxLineLength + @"}",  m => m.Value.EndsWith("\n") ? m.Value + "\t" : m.Value + "\n\t");
        }
#else
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
#endif
    }
}
