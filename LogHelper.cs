using System;
using System.Collections;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Text;

namespace Logging
{
    /// <summary>
    /// This class is used to Log Infos, and Exceptions. 
    /// This could be used to trace the application on production
    /// Logs are stored as csv file.
    /// </summary>
    public sealed class LogHelper
    {
        private static volatile LogHelper _instance;
        private static object _lock = new Object();

        private string _trace = "false";
        private int _level = 2;
        private int _depth = 1;
        private string _path = string.Empty;
        private string _logIn = string.Empty;
        private string _AppName = string.Empty;
        private EventLog _eventLog;

        #region Public Static Method
        /// <summary>
        /// Singleton Constructor
        /// </summary>
        private LogHelper()
        {
            if (ConfigurationManager.AppSettings.HasKeys())
            {
                _trace = ConfigurationManager.AppSettings["Trace"] ?? "false";
                _path = ConfigurationManager.AppSettings["LogPath"] ?? AppDomain.CurrentDomain.BaseDirectory;
                _logIn = ConfigurationManager.AppSettings["LogIn"] ?? "Windows";
                _AppName = ConfigurationManager.AppSettings["EventSource"] ?? Process.GetCurrentProcess().MainModule.FileName;
            }

            if (_logIn.Trim().ToLower() == "windows" || _logIn.Trim().ToLower() == "both")
            {
                SetupWindowsEvent();
            }
            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["Level"]))
            {
                if (!int.TryParse(ConfigurationManager.AppSettings["Level"].ToString(), out _level))
                {
                    _level = 1;
                }
            }
            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["Depth"]))
            {
                if (!int.TryParse(ConfigurationManager.AppSettings["Depth"].ToString(), out _depth))
                {
                    _depth = 1;
                }
            }
        }

        /// <summary>
        /// Set Windows event, to log infos in event viewer
        /// </summary>
        private void SetupWindowsEvent()
        {
            if (!EventLog.SourceExists(_AppName))
                EventLog.CreateEventSource(_AppName, "Application");
            _eventLog = new EventLog();
            _eventLog.Source = _AppName;
        }

        /// <summary>
        /// Properties to get the singleton object
        /// </summary>
        public static LogHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LogHelper();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Public Write Method

        /// <summary>
        /// Write info of the called method. 
        /// </summary>
        public void WriteLog(string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name;

            WriteLog(LogType.Information, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write info of the called method with generic parameter
        /// </summary>
        public void WriteGenericLog<T>(T request, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string message = methodBase.DeclaringType.FullName + ":" + methodBase.Name;

            Dictionary<string, string> data = null;


            Type t = typeof(T);

            if (t != typeof(string) && t != typeof(object) && !t.IsValueType && !t.IsPrimitive)
            {
                data = new Dictionary<string, string>();
                if (request is IEnumerable)
                {
                    int idx = 0;
                    var collection = request as IEnumerable;

                    if (collection != null)
                    {
                        foreach (object obj in collection)
                        {
                            Type objType = obj.GetType();
                            data = AddDataLog(username, objType, objType.Name, obj, data, "#" + idx.ToString());
                            idx++;
                        }
                    }
                }
                else
                {
                    PropertyInfo[] properties = t.GetProperties();
                    foreach (PropertyInfo pi in properties)
                    {
                        try
                        {

                            data = AddDataLog(username, pi.PropertyType, pi.Name, request == null ? null : pi.GetValue(request, null), data, string.Empty);
                        }
                        catch (Exception ex)
                        {
                            WriteException(ex, username);
                            continue;
                        }
                    }
                }
            }
            else
            {
                message += ":" + Convert.ToString(request);
            }

            WriteLog(LogType.Information, username, AppDepth, message, data);
        }

        /// <summary>
        /// Dictionary constructor, to get all properties of the generic parameter
        /// </summary>
        private Dictionary<string, string> AddDataLog(string username, Type t, string key, object value, Dictionary<string, string> dataLog, string index)
        {
            if (t != typeof(string) && t != typeof(object) && !t.IsValueType && !t.IsPrimitive && value != null)
            {
                if (value is IEnumerable)
                {
                    int idx = 0;
                    var collection = value as IEnumerable;

                    if (collection != null)
                    {
                        foreach (object obj in collection)
                        {
                            Type objType = obj.GetType();
                            dataLog = AddDataLog(username, objType, key + "#" + index + "_" + objType.Name, obj, dataLog, "#" + idx.ToString());
                            idx++;
                        }
                    }
                }
                else
                {
                    PropertyInfo[] properties = t.GetProperties();
                    foreach (PropertyInfo pi in properties)
                    {
                        try
                        {
                            dataLog = AddDataLog(username, pi.PropertyType, key + "#" + index + "_" + pi.Name, pi.GetValue(value, null), dataLog, index);
                        }
                        catch (Exception ex)
                        {
                            WriteException(ex, username);
                            continue;
                        }
                    }
                }

            }
            else
            {
                dataLog.Add(key + "#" + index, Convert.ToString(value));
            }

            return dataLog;
        }

        /// <summary>
        /// Write info of the called method with specified data
        /// </summary>
        public void WriteLog(IDictionary data, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string message = methodBase.DeclaringType.FullName + ":" + methodBase.Name;
            WriteLog(LogType.Information, username, AppDepth, message, data);
        }

        /// <summary>
        /// Write info of the called method with message
        /// </summary>
        /// <param name="message">message to log</param>
        public void WriteLog(string message, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + message;
            WriteLog(LogType.Information, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write warning in the called method with warning message
        /// </summary>
        /// <param name="message">warning message to log</param>
        public void WriteWarningLog(string message, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + message;
            WriteLog(LogType.Warning, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write error of the called method with error message
        /// </summary>
        /// <param name="message">error message to log</param>
        public void WriteErrorLog(string message, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + message;
            WriteLog(LogType.Error, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write warning in the called method with warning message
        /// </summary>
        /// <param name="message">warning message to log</param>
        public void WriteDebugLog(string message, string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + message;
            WriteLog(LogType.Debug, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write success message at the end of called method to indcate method doesn't return error
        /// </summary>
        public void WriteSuccessLog(string username, int AppDepth)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + "Command Succeeded";
            WriteLog(LogType.Information, username, AppDepth, log, null);
        }

        /// <summary>
        /// Write info of th called method with message and specified data
        /// </summary>
        /// <param name="message">message to log</param>
        /// <param name="data">data to log</param>
        public void WriteLog(string message, string username, int AppDepth, IDictionary data)
        {
            if (!CheckConfig()) return;

            StackTrace stackTrace = new StackTrace();
            StackFrame stackFrame = stackTrace.GetFrame(1);
            MethodBase methodBase = stackFrame.GetMethod();
            string log = methodBase.DeclaringType.FullName + ":" + methodBase.Name + ":" + message;
            WriteLog(LogType.Information, username, AppDepth, log, data);

        }

        /// <summary>
        /// Write logs to file/windows event
        /// </summary>
        /// <param name="logType">info, warning, error</param>
        /// <param name="message">message to log</param>
        /// <param name="data">data to log</param>
        private void WriteLog(LogType logType, string username, int AppDepth, string message, IDictionary data)
        {
            string day = DateTime.Today.ToString("yyyy-MM-dd");
            int threadID = Thread.CurrentThread.ManagedThreadId;
            string logFileName = _path + "\\" + username + "\\" + day + "-" + threadID + ".log";

            try
            {
                if (_logIn.ToLower().Trim() == "file" || _logIn.ToLower().Trim() == "both")
                {
                    if (!System.IO.File.Exists(logFileName))
                    {

                        if (!System.IO.Directory.Exists(_path))
                        {
                            System.IO.Directory.CreateDirectory(_path);
                        }
                        if (!System.IO.Directory.Exists(_path + "\\" + username))
                        {
                            System.IO.Directory.CreateDirectory(_path + "\\" + username);
                        }
                        System.IO.File.Create(logFileName).Close();
                    }

                    if (((int)logType <= _level) && (AppDepth <=_depth))
                    {
                        using (System.IO.StreamWriter w = System.IO.File.AppendText(logFileName))
                        {
                            w.Write("Time-{0},", DateTime.Now);
                            w.Write("Type-{0},", logType.ToString());
                            if (data != null)
                            {
                                w.Write("Message-{0},", message);
                                w.Write("\"Details-");
                                foreach (DictionaryEntry de in data)
                                {
                                    w.WriteLine("{0}:{1}", de.Key, de.Value);
                                }
                                w.WriteLine("\"");
                            }
                            else
                            {
                                w.WriteLine("Message-{0}", message);
                            }
                            w.Flush();
                            w.Close();
                        }
                    }

                    if (_logIn.ToLower().Trim() == "windows" || _logIn.ToLower().Trim() == "both")
                    {
                        if (_eventLog == null)
                            SetupWindowsEvent();
                        switch (logType)
                        {
                            case LogType.Critical:
                            case LogType.Error:
                                _eventLog.WriteEntry(message, EventLogEntryType.Error);
                                break;
                            case LogType.Warning:
                                _eventLog.WriteEntry(message, EventLogEntryType.Warning);
                                break;
                            default:
                                _eventLog.WriteEntry(message, EventLogEntryType.Information);
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                WriteException(e, username);
            }
        }

        /// <summary>
        /// write exception to file/windows event
        /// </summary>
        /// <param name="ex">exception to log</param>
        public void WriteException(Exception ex, string username)
        {
            string day = DateTime.Today.ToString("yyyy-MM-dd");
            int threadID = Thread.CurrentThread.ManagedThreadId;
            string logFileName = _path + "\\" + username + "\\" + day + "-" + threadID + ".log";
            if (!System.IO.File.Exists(logFileName))
            {
                if (!System.IO.Directory.Exists(_path))
                {
                    System.IO.Directory.CreateDirectory(_path);
                }
                if (!System.IO.Directory.Exists(_path + "\\" + username))
                {
                    System.IO.Directory.CreateDirectory(_path + "\\" + username);
                }
                System.IO.File.Create(logFileName).Close();
            }
            Exception innerException = null;
            using (System.IO.StreamWriter w = System.IO.File.AppendText(logFileName))
            {
                w.Write("Time-{0},", DateTime.Now);
                w.Write("Type-{0},", LogType.Critical.ToString());
                w.Write("Message-{0},", ex.Message);
                w.WriteLine("\"Details-");
                foreach (DictionaryEntry de in ex.Data)
                {
                    w.WriteLine("{0}:{1}", de.Key, de.Value);
                }
                w.WriteLine();
                w.Write("HelpLink-{0};", ex.HelpLink);
                w.Write("Source-{0};", ex.Source);
                w.WriteLine("StackTrace-\r\n{0}", ex.StackTrace);

                innerException = ex.InnerException;
                if (innerException != null)
                {
                    w.WriteLine("TargetSite-{0}", innerException.TargetSite);
                    while (innerException != null)
                    {
                        w.WriteLine("Inner Exception-");
                        w.Write("Message-{0},", innerException.Message);
                        w.WriteLine("Details-");
                        foreach (DictionaryEntry de in innerException.Data)
                        {
                            w.WriteLine("{0}:{1};", de.Key, de.Value);
                        }

                        w.Write("HelpLink-{0};", innerException.HelpLink);
                        w.Write("Source-{0};", innerException.Source);
                        w.WriteLine("StackTrace-\r\n{0}", innerException.StackTrace);
                        w.WriteLine("TargetSite-{0}", innerException.TargetSite);
                        innerException = innerException.InnerException;
                    }
                }
                else
                {
                    w.WriteLine("TargetSite-{0}\"", ex.TargetSite);
                }
                w.Flush();
                w.Close();
            }
            if (_eventLog == null)
                SetupWindowsEvent();
            StringBuilder eventMessage = new StringBuilder();
            eventMessage.AppendFormat("Message-{0}\r\n", ex.Message);
            eventMessage.AppendFormat("Source-{0}\r\n", ex.Source);
            foreach (DictionaryEntry de in ex.Data)
            {
                eventMessage.AppendFormat("Data-{0}:{1}\r\n", de.Key, de.Value);
            }
            eventMessage.AppendFormat("TargetSite-{0}\r\n", ex.TargetSite);
            eventMessage.AppendFormat("StackTrace-{0}\r\n", ex.StackTrace);
            eventMessage.AppendFormat("HelpLink-{0}\r\n;", ex.HelpLink);
            innerException = ex.InnerException;
            while (innerException != null)
            {
                eventMessage.AppendFormat("Message-{0}\r\n", innerException.Message);
                eventMessage.AppendFormat("Source-{0}\r\n", innerException.Source);
                foreach (DictionaryEntry de in innerException.Data)
                {
                    eventMessage.AppendFormat("Data-{0}:{1}\r\n", de.Key, de.Value);
                }
                eventMessage.AppendFormat("TargetSite-{0}\r\n", innerException.TargetSite);
                eventMessage.AppendFormat("StackTrace-{0}\r\n", innerException.StackTrace);
                eventMessage.AppendFormat("HelpLink-{0}\r\n;", innerException.HelpLink);
                innerException = innerException.InnerException;
            }
            _eventLog.WriteEntry(eventMessage.ToString(), EventLogEntryType.Error);
        }

        /// <summary>
        /// check if the config has been set, trace == true means logging is enabled.
        /// </summary>
        /// <returns></returns>
        private bool CheckConfig()
        {
            return (_trace.ToLower() == "true");
        }

        public enum LogType
        {
            Critical,
            Information,
            Error,
            Warning,
            Debug
        }

        public enum LoggingLevel
        {
            CriticalOnly = 0,
            InfoOnly = 1,
            ErrorOnly = 2,
            WarningOnly = 3,
            AllInfo = 4
        }
        #endregion
    }
}
