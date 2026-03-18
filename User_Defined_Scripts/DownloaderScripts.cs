using HtmlAgilityPack;
using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Scripts.Scripting
{
    [ScriptCollectionName("Downloading Tools")]
    [ScriptCollectionDescription("Collection of Scripts for downloading files or information based on a list or other characteristic.")]
    public class DownloaderScripts : IScriptCollection
    {
        [ScriptRoutine("Download Files based on HTML Href and Extension", "Queries an address and grabs an html manefest to do a bulk download.")]
        public static void BulkDownloader(
            IScriptContext context,
            [Parameter("html homme", "The HTML file to parse", @"s")]
            string url,
            [Parameter("Output Path", "The folder where the files will be downloaded too.")]
            DirectoryInfo path)
        {
            if (!path.Exists)
            {
                path.Create();
            }

            using (var client = new WebClient())
            {
                client.DownloadFile(url, "read.htm");
            }

            FileInfo fInfo = new FileInfo("read.htm");

            HtmlDocument parser = new HtmlDocument();
            parser.Load(fInfo.OpenRead()); 
            
            foreach (var node in parser.DocumentNode.SelectNodes("//a[@href]"))
            {
                string value = node.GetAttributeValue("href", null);
                if (value != null && value.EndsWith(".zip"))
                {
                    Logger.WriteLine(Logger.LogLevel.Event, "Downloading: " + value.Trim());
                    string pathToSave = Path.Combine(path.FullName, value);
                    FileInfo dlInfo = new FileInfo(pathToSave);

                    if (!dlInfo.Exists)
                    {
                        try
                        {
                            using (var client = new WebClient())
                            {
                                client.DownloadFile(url + value, pathToSave);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLine(Logger.LogLevel.Error, "Unable to save: " + pathToSave);
                            Logger.WriteLine(Logger.LogLevel.Error, ex.ToString());

                        }
                    }
                }
            }
        }
    }
}
