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
            DirectoryInfo installPath,
           [Parameter("Download Path", "The location to download the archive to", "s", "D:\\Downloads\\keydb.cfg.zip")]
            FileInfo downloadPath)
        {
            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource();
                var downloadTask = context.CreateProgressChannel("Download keydb archive");
                DownloadWithProgressAsync(url, downloadPath, downloadTask, cts.Token).GetAwaiter().GetResult();
                downloadTask.Complete("Download complete");

                Logger.WriteLine(Logger.LogLevel.Event, "Download Complete, Extracting now...");

                var extractTask = context.CreateProgressChannel("Extract keydb archive");
                extractTask.Report(15, "Preparing extraction");

                if (!installPath.Exists)
                {
                    installPath.Create();
                }

                var keyDbPath = Path.Combine(installPath.FullName, "keydb.cfg");
                if (File.Exists(keyDbPath))
                {
                    extractTask.Report(40, "Removing existing keydb.cfg");
                    File.Delete(keyDbPath);
                }

                extractTask.Report(70, "Extracting archive");
                ZipFile.ExtractToDirectory(downloadPath.FullName, installPath.FullName, overwriteFiles: true);
                extractTask.Complete("Extraction complete");
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

        private static async Task DownloadWithProgressAsync(string url, FileInfo dest, ScriptProgressChannel progress, CancellationToken ct)
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            if (dest.Directory != null && !dest.Directory.Exists)
            {
                dest.Directory.Create();
            }

            await using var destination = File.Create(dest.FullName);

            var buffer = new byte[64 * 1024];
            long totalRead = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (contentLength != -1)
                {
                    progress.Report((double)totalRead / contentLength * 100, "Downloading keydb archive");
                }
            }
        }
    }
}

