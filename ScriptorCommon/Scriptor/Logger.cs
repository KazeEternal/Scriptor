using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor
{
    public static class Logger
    {
        public enum LogLevel
        {
            Event,
            Warning,
            Error,
        }

        public delegate void LogEventHandler(string format, params object[] args);
        public static event LogEventHandler Error;
        public static event LogEventHandler Warning;
        public static event LogEventHandler Event;

        public static void Write(LogLevel level, string format, params object[] args)
        {
            switch (level)
            {
                case LogLevel.Event:
                    if(Event != null) Event(format, args);
                    break;
                case LogLevel.Warning:
                    if(Warning != null) Warning(format, args);
                    break;
                case LogLevel.Error:
                    if(Error != null) Error(format, args);
                    break;
                default:
                    if(Event != null) Event(String.Format("[{0}]", level.ToString() , format), args);
                    break;
            }
        }
        public static void WriteLine(LogLevel level, string format, params object[] args)
        {
            Write(level, format +"\n", args);
        }
    }
}
