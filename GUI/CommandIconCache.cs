using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GUI.ViewModel;

namespace GUI
{
    internal static class CommandIconCache
    {
        private const int MaximumIconSizeBytes = 512 * 1024;
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

        public static async Task<string?> CacheWebsiteIconAsync(string scriptsRoot, CommandDefinition command)
        {
            if (command.Type != CommandType.Url
                || !string.IsNullOrWhiteSpace(command.IconPath)
                || !Uri.TryCreate(command.Target, UriKind.Absolute, out var target)
                || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var iconDirectory = Path.Combine(scriptsRoot, ".scriptor", "command-icons");
            var iconPath = Path.Combine(iconDirectory, $"{command.Id}.ico");
            if (File.Exists(iconPath))
            {
                return iconPath;
            }

            try
            {
                using var response = await HttpClient.GetAsync(new Uri(target, "/favicon.ico"), HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaximumIconSizeBytes)
                {
                    return null;
                }

                await using var input = await response.Content.ReadAsStreamAsync();
                Directory.CreateDirectory(iconDirectory);
                var temporaryPath = $"{iconPath}.{Guid.NewGuid():N}.tmp";
                var completed = false;
                try
                {
                    await using var output = File.Create(temporaryPath);
                    var buffer = new byte[81920];
                    var totalBytes = 0;
                    int bytesRead;
                    while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None)) > 0)
                    {
                        totalBytes += bytesRead;
                        if (totalBytes > MaximumIconSizeBytes)
                        {
                            return null;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, bytesRead), CancellationToken.None);
                    }

                    completed = true;
                }
                finally
                {
                    if (completed && File.Exists(temporaryPath) && !File.Exists(iconPath))
                    {
                        File.Move(temporaryPath, iconPath);
                    }
                    else if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                return File.Exists(iconPath) ? iconPath : null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
