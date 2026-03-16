using System;

namespace Scripts.Scriptor
{
    public sealed class ScriptProgressChannel
    {
        private readonly Action<double, string> _report;

        internal ScriptProgressChannel(string key, string displayName, Action<double, string> report)
        {
            Key = key;
            DisplayName = displayName;
            _report = report;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public void Report(double progressValue, string message = "")
        {
            _report(progressValue, message);
        }

        public void Complete(string message = "Completed")
        {
            _report(100, message);
        }

        public void LogInfo(string format, params object[] args)
        {
            Logger.WriteToProgress(Key, Logger.LogLevel.Event, format, args);
        }

        public void LogWarning(string format, params object[] args)
        {
            Logger.WriteToProgress(Key, Logger.LogLevel.Warning, format, args);
        }

        public void LogError(string format, params object[] args)
        {
            Logger.WriteToProgress(Key, Logger.LogLevel.Error, format, args);
        }
    }
}
