using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Scripts.Scripting
{
    [ScriptCollectionName("Feature Validation")]
    [ScriptCollectionDescription("Routines to validate run log rows, status transitions, failures, and progress bar behavior.")]
    public class FeatureValidationScripts : IScriptCollection
    {
        public enum DemoMode
        {
            Fast,
            Balanced,
            Safe,
        }

        [ScriptRoutine("Progress Demo (Single)", "Runs a single-threaded progress loop and emits status messages.")]
        public static void ProgressDemoSingle(
            IScriptContext context,
            [Parameter("Steps", "Number of progress steps.", "Higher values produce smoother progress updates.", 20)] int steps,
            [Parameter("Delay (ms)", "Delay per step.", "How long each step waits before next update.", 150)] int delayMs)
        {
            steps = Math.Max(1, steps);
            delayMs = Math.Max(1, delayMs);

            Logger.WriteLine(Logger.LogLevel.Event, "Starting single progress demo.");
            var overall = context.CreateProgressChannel("Overall");
            for (var i = 1; i <= steps; i++)
            {
                Thread.Sleep(delayMs);
                var pct = (i * 100.0) / steps;
                overall.Report(pct, "Single routine progress");
                Logger.WriteLine(Logger.LogLevel.Event, "Step {0}/{1}", i, steps);
            }

            Logger.WriteLine(Logger.LogLevel.Event, "Single progress demo complete.");
        }

        [ScriptRoutine("Progress Demo (Multi-thread)", "Runs multiple workers that each report progress under separate keys.")]
        public static void ProgressDemoMultiThread(
            IScriptContext context,
            [Parameter("Workers", "How many worker threads to run.", "Each worker gets its own progress bar.", 3)] int workers,
            [Parameter("Delay (ms)", "Delay between worker updates.", "Controls how fast each worker advances.", 120)] int delayMs)
        {
            workers = Math.Max(1, workers);
            delayMs = Math.Max(1, delayMs);
            const int totalDurationMs = 10_000;

            Logger.WriteLine(Logger.LogLevel.Event, "Starting multi-thread progress demo with {0} workers for ~10s.", workers);

            Parallel.For(0, workers, workerIndex =>
            {
                var workerName = $"Worker {workerIndex + 1}";
                var worker = context.CreateProgressChannel(workerName);
                worker.LogInfo("{0} started.", workerName);
                var started = Environment.TickCount64;
                var lastBucket = -1;
                while (true)
                {
                    var elapsed = Environment.TickCount64 - started;
                    var pct = Math.Min(100.0, (elapsed * 100.0) / totalDurationMs);
                    worker.Report(pct, workerName);

                    var bucket = (int)(pct / 25);
                    if (bucket > lastBucket)
                    {
                        lastBucket = bucket;
                        worker.LogInfo("{0} reached {1:0.#}%.", workerName, pct);
                    }

                    if (elapsed >= totalDurationMs)
                    {
                        break;
                    }

                    Thread.Sleep(delayMs + (workerIndex * 15));
                }

                worker.LogInfo("{0} completed.", workerName);

            });

            Logger.WriteLine(Logger.LogLevel.Event, "Multi-thread progress demo complete.");
        }

        [ScriptRoutine("Failure Demo", "Reports progress and then throws to test failed status rendering.")]
        public static void FailureDemo(
            IScriptContext context,
            [Parameter("Fail At Percent", "Progress percentage at which to fail.", "Must be between 1 and 99.", 65)] int failAtPercent,
            [Parameter("Delay (ms)", "Delay between updates.", "Controls speed of progress updates.", 120)] int delayMs)
        {
            failAtPercent = Math.Clamp(failAtPercent, 1, 99);
            delayMs = Math.Max(1, delayMs);

            Logger.WriteLine(Logger.LogLevel.Warning, "Failure demo started. It will intentionally fail at {0}%.", failAtPercent);
            var failure = context.CreateProgressChannel("Failure Demo");
            for (var pct = 0; pct <= 100; pct += 5)
            {
                Thread.Sleep(delayMs);
                failure.Report(pct, "Failure demo progress");

                if (pct >= failAtPercent)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "Intentional failure triggered at {0}%.", pct);
                    throw new InvalidOperationException($"Intentional test failure at {pct}%");
                }
            }
        }

        [ScriptRoutine("Parameter UI Demo", "Demonstrates typed parameter editors: checkbox, enum dropdown, numeric, slider, file/folder picker, password, and multiline text.")]
        public static void ParameterUiDemo(
            IScriptContext context,
            [Parameter("Enable Feature", "Boolean demo parameter.", "ui:checkbox", true)] bool enableFeature,
            [Parameter("Mode", "Enum demo parameter.", "Dropdown enum selection", DemoMode.Balanced)] DemoMode mode,
            [Parameter("Retry Count", "Integer demo parameter.", "Numeric editor", 3)] int retryCount,
            [Parameter("Threshold", "Decimal demo parameter.", "ui:slider(0,1,0.05)", 0.7)] decimal threshold,
            [Parameter("Timeout Seconds", "Long demo parameter.", "Numeric editor", 30L)] long timeoutSeconds,
            [Parameter("Source File", "File picker demo parameter.", "ui:file", "D:\\Temp\\input.txt")] FileInfo sourceFile,
            [Parameter("Output Folder", "Folder picker demo parameter.", "ui:folder", "D:\\Temp")] DirectoryInfo outputFolder,
            [Parameter("Api Key", "Password-mask demo parameter.", "ui:password", "")]
            string apiKey,
            [Parameter("Notes", "Multiline text demo parameter.", "ui:multiline", "Enter notes here...")]
            string notes)
        {
            var task = context.CreateProgressChannel("Parameter UI Demo");
            task.Report(100, "Captured parameter values");

            Logger.WriteLine(Logger.LogLevel.Event, "UI Demo Values:");
            Logger.WriteLine(Logger.LogLevel.Event, "  Enable Feature : {0}", enableFeature);
            Logger.WriteLine(Logger.LogLevel.Event, "  Mode           : {0}", mode);
            Logger.WriteLine(Logger.LogLevel.Event, "  Retry Count    : {0}", retryCount);
            Logger.WriteLine(Logger.LogLevel.Event, "  Threshold      : {0}", threshold);
            Logger.WriteLine(Logger.LogLevel.Event, "  Timeout Seconds: {0}", timeoutSeconds);
            Logger.WriteLine(Logger.LogLevel.Event, "  Source File    : {0}", sourceFile?.FullName ?? "<null>");
            Logger.WriteLine(Logger.LogLevel.Event, "  Output Folder  : {0}", outputFolder?.FullName ?? "<null>");
            Logger.WriteLine(Logger.LogLevel.Event, "  Api Key Length : {0}", string.IsNullOrEmpty(apiKey) ? 0 : apiKey.Length);
            Logger.WriteLine(Logger.LogLevel.Event, "  Notes Length   : {0}", string.IsNullOrEmpty(notes) ? 0 : notes.Length);
        }
    }
}
