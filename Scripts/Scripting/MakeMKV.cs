using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Scripts.Scripting
{
    [ScriptCollectionName("Bluray Ripping Tools")]
    [ScriptCollectionDescription("Collection of Scripts related to ripping of movie discs and related activities.")]
    public class MakeMKV : IScriptCollection
    {
        [ScriptRoutine("Update MakeMKV's Encryption Keys", "Reaches out to a hard coded address with the keys and extracts to the directory they live in for keydb.cfg")]
        public static void UpdateDecryptionKeys(
           IScriptContext context,
           [Parameter("link to the keydb.cfg", "the home of the file to download", @"s", "http://fvonline-db.bplaced.net/fv_download.php?lang=eng")]
            string url,
           [Parameter("Output Path", "The location to place the cfg file.", "s", "C:\\Users\\Kyle Peplow\\.MakeMKV")]
            string installPath,
           [Parameter("Download Path", "The location to download the archive to", "s", "D:\\Downloads\\keydb.cfg.zip")]
            string downloadPath)
        {
            try
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var prog = new Progress<double>(p => Console.Write( $"\rDownload Progress: {p:F1}%"));
                DownloadWithProgressAsync(url, downloadPath, prog, cts.Token).GetAwaiter().GetResult();

                Logger.WriteLine(Logger.LogLevel.Event, "Download Complete, Extracting now...");

                if (File.Exists(Path.Combine(installPath, "keydb.cfg")))
                {
                    File.Delete(Path.Combine(installPath, "keydb.cfg"));
                }

                ZipFile.ExtractToDirectory(downloadPath, installPath, overwriteFiles: true);
                Logger.WriteLine(Logger.LogLevel.Event, "Extraction complete!");
            }
            catch (Exception ex)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Error Downloading File: " + ex.ToString());
                context.IsSuccess = false;
                return;
            }

            context.IsSuccess = true;
        }

        private static async Task DownloadWithProgressAsync(string url, string dest, IProgress<double> progress, CancellationToken ct)
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = File.Create(dest);

            var buffer = new byte[100000000];
            long totalRead = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (contentLength != -1)
                    progress?.Report((double)totalRead / contentLength * 100);
            }
        }
    }
}

