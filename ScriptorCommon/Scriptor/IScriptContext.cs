using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scripts.Scriptor
{
    public class IScriptContext
    {
        private int _progressChannelCounter;

        public bool IsSuccess { get; set; } = true;
        public string Name { get; set; }

        public void ReportProgress(string progressKey, double progressValue, string message = "")
        {
            Logger.WriteProgress(progressKey, progressValue, message);
        }

        public ScriptProgressChannel CreateProgressChannel(string displayName)
        {
            var safeName = string.IsNullOrWhiteSpace(displayName) ? "task" : displayName.Trim();
            var channelId = Interlocked.Increment(ref _progressChannelCounter);
            var key = $"{safeName}:{channelId}";

            return new ScriptProgressChannel(
                key,
                safeName,
                (value, message) => ReportProgress(key, value, string.IsNullOrWhiteSpace(message) ? safeName : message));
        }
    }
}
