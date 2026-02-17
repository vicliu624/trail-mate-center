namespace RJCP.Diagnostics.Trace
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

#if NETSTANDARD
    using Microsoft.Extensions.Logging;
#endif

    /// <summary>
    /// A <see cref="TraceSource"/> wrapper for better performance tracing.
    /// </summary>
    /// <remarks>
    /// Use the <see cref="LogSource"/> class instead of a <see cref="System.Diagnostics.TraceSource"/> class to
    /// initialize logging, so that tests can be done with the <see cref="ShouldTrace(TraceEventType)"/> method.
    /// <para>
    /// The goal is to allow classes to test if tracing is enabled or not, before using the <see cref="TraceSource"/>.
    /// This can help prevention of unnecessary objects on the heap by tracing only when tracing is needed.
    /// </para>
    /// <code language="csharp">
    ///<![CDATA[
    ///private static LogSource log = new LogSource("MyTrace");
    ///if (log.ShouldTrace(TraceEventType.Information))
    ///  log.TraceSource.TraceEvent(TraceEventType.Information, id, $"Log Message: {errorCode}");
    ///]]>
    /// </code>
    /// <para>
    /// This allows first a check if tracing is allowed or not, by doing a bitwise comparison, and only then if tracing
    /// is enabled, will the TraceEvent function be called thus allocating the <c>object[]</c> for params, then boxing
    /// the integer value <c>errorCode</c> into the <c>object[]</c> array.
    /// </para>
    /// </remarks>
    public sealed class LogSource : IDisposable
    {
        private readonly string m_Name;
        private readonly long m_TraceLevels;
        private TraceSource m_TraceSource;

        private static readonly object TraceSourceLock = new object();
        private static readonly Dictionary<string, TraceSource> TraceSources = new Dictionary<string, TraceSource>();

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="source"/>.
        /// </summary>
        /// <param name="source">
        /// The <see cref="System.Diagnostics.TraceSource"/> that should be returned when queried.
        /// </param>
        /// <returns>
        /// If the <paramref name="source"/> was already defined (based on the category given in
        /// <see cref="TraceSource.Name"/>), returns <see langword="true"/> indicating it was overwritten, else
        /// <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Use this method to override setting a <see cref="System.Diagnostics.TraceSource"/> for a specific category
        /// before it is requested, as an alternative to using a configuration.
        /// </remarks>
        public static bool SetLogSource(TraceSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            lock (TraceSourceLock) {
                string name = source.Name;
                if (TraceSources.ContainsKey(name)) {
                    TraceSources[name] = source;
                    return true;
                }
                TraceSources.Add(name, source);
                return false;
            }
        }

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="name"/> to a particular <paramref name="listener"/>.
        /// </summary>
        /// <param name="name">The category name of the log source.</param>
        /// <param name="level">The switch trace level.</param>
        /// <param name="listener">The trace listener.</param>
        /// <returns>
        /// If the <paramref name="name"/> was already defined, returns <see langword="true"/> indicating it was
        /// overwritten, else <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentNullException">listener</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is an empty string.</exception>
        /// <remarks>
        /// Use this method to override setting a <see cref="System.Diagnostics.TraceSource"/> for a specific category
        /// before it is requested, as an alternative to using a configuration.
        /// </remarks>
        public static bool SetLogSource(string name, SourceLevels level, TraceListener listener)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Source name empty", nameof(name));
            if (listener == null) throw new ArgumentNullException(nameof(listener));

            TraceSource source = new TraceSource(name) {
                Switch = new SourceSwitch(name) {
                    Level = level
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(listener);

            lock (TraceSourceLock) {
                if (TraceSources.ContainsKey(name)) {
                    TraceSources[name] = source;
                    return true;
                }
                TraceSources.Add(name, source);
                return false;
            }
        }

#if NETSTANDARD
        private static ILoggerFactory LoggerFactory;
        private static readonly Dictionary<string, ILogger> Loggers = new Dictionary<string, ILogger>();

        /// <summary>
        /// Remove a <see cref="ILoggerFactory"/> if set with <see cref="SetLoggerFactory(ILoggerFactory)"/>.
        /// </summary>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/> indicating it was overwritten, else
        /// <see langword="false"/>.
        /// </returns>
        public static bool ClearLoggerFactory()
        {
            lock (TraceSourceLock) {
                bool predefined = LoggerFactory != null;
                LoggerFactory = null;
                return predefined;
            }
        }

        /// <summary>
        /// Set a Logger Factory to initialize a TraceFactory for a given category.
        /// </summary>
        /// <param name="loggerFactory">
        /// The <see cref="ILoggerFactory"/> that can be used to create an <see cref="ILogger"/> for a given category.
        /// </param>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/> indicating it was not overwritten,
        /// else <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            return SetLoggerFactory(loggerFactory, false);
        }

        /// <summary>
        /// Set a Logger Factory to initialize a TraceFactory for a given category.
        /// </summary>
        /// <param name="loggerFactory">
        /// The <see cref="ILoggerFactory"/> that can be used to create an <see cref="ILogger"/> for a given category.
        /// </param>
        /// <param name="overrideFactory">
        /// Set to <see langword="true"/> to override the factory if it is already set. If <see langword="false"/>, then
        /// the existing factory is not overridden.
        /// </param>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/>. If
        /// <paramref name="overrideFactory"/> is <see langword="true"/>, then it is overwritten. else
        /// <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLoggerFactory(ILoggerFactory loggerFactory, bool overrideFactory)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            lock (TraceSourceLock) {
                bool predefined = LoggerFactory != null;
                if (!predefined || overrideFactory) LoggerFactory = loggerFactory;
                return predefined;
            }
        }

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="name"/> to a particular <paramref name="logger"/>.
        /// </summary>
        /// <param name="name">The category name of the log source.</param>
        /// <param name="logger">
        /// The <see cref="ILogger"/> that should be wrapped around a <see cref="System.Diagnostics.TraceSource"/>.
        /// </param>
        /// <returns>
        /// If the <paramref name="name"/> was already defined, returns <see langword="true"/> indicating it was
        /// overwritten, else <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLogSource(string name, ILogger logger)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Source name empty", nameof(name));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            TraceSource source = new TraceSource(name) {
                Switch = new SourceSwitch(name) {
                    Level = GetSourceLevels(logger)
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(new LoggerTraceListener(name, logger));

            lock (TraceSourceLock) {
                if (TraceSources.ContainsKey(name)) {
                    Loggers[name] = logger;
                    TraceSources[name] = source;
                    return true;
                }
                Loggers.Add(name, logger);
                TraceSources.Add(name, source);
                return false;
            }
        }

        private static SourceLevels GetSourceLevels(ILogger logger)
        {
            if (logger == null) return SourceLevels.Off;

            if (logger.IsEnabled(LogLevel.Trace)) return SourceLevels.All;
            if (logger.IsEnabled(LogLevel.Debug)) return SourceLevels.Verbose;
            if (logger.IsEnabled(LogLevel.Information)) return SourceLevels.Information;
            if (logger.IsEnabled(LogLevel.Warning)) return SourceLevels.Warning;
            if (logger.IsEnabled(LogLevel.Error)) return SourceLevels.Error;
            if (logger.IsEnabled(LogLevel.Critical)) return SourceLevels.Critical;
            return SourceLevels.Off;
        }
#endif

        /// <summary>
        /// Clears all cached log sources
        /// </summary>
        public static void ClearLogSource()
        {
            lock (TraceSourceLock) {
                TraceSources.Clear();
#if NETSTANDARD
                Loggers.Clear();
#endif
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogSource"/> class that doesn't log.
        /// </summary>
        /// <remarks>
        /// Provides a constructor for a logger object that doesn't log. The implementation is not completely empty, a
        /// <see cref="TraceSource"/> is still available but effectively logging is disabled. It is possible to
        /// manipulate the <see cref="TraceSource"/> property to enable logging. The name of the trace source is
        /// <c>default</c>.
        /// </remarks>
        public LogSource()
        {
            m_Name = string.Empty;
            m_TraceSource = new TraceSource("default") {
                Switch = new SourceSwitch("default") {
                    Level = SourceLevels.Off
                }
            };
            m_TraceSource.Listeners.Clear();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogSource"/> class with the
        /// <see cref="System.Diagnostics.TraceSource"/> of the name provided.
        /// </summary>
        /// <param name="name">The name of the trace source.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> may not be empty.</exception>
        public LogSource(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name may not be empty", nameof(name));

            lock (TraceSourceLock) {
                m_Name = name;
                InitLogSource(name);
                m_TraceLevels = GetTraceLevels(m_TraceSource);
            }
        }

#if NETSTANDARD
        /// <summary>
        /// Create a
        /// </summary>
        /// <param name="name">
        /// The name of the logger that should be used. This can be the name of your library for example.
        /// </param>
        /// <param name="logger">
        /// The <see cref="ILogger"/> that should be wrapped within a <see cref="System.Diagnostics.TraceSource"/>
        /// object for logging.
        /// </param>
        /// <remarks>
        /// This constructor is to create a logger directly from your own constructor. It is not registered and used
        /// directly as it is, which is useful for .NET Core code that takes its logger through the constructor.
        /// </remarks>
        [CLSCompliant(false)]
        public LogSource(string name, ILogger logger)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Logger name is empty", nameof(name));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            m_TraceSource = CreateFromLogger(name, logger);
            Logger = logger;
            m_TraceLevels = GetTraceLevels(m_TraceSource);
        }

        private static TraceSource CreateFromLogger(string name, ILogger logger)
        {
            TraceSource source = new TraceSource(name) {
                Switch = new SourceSwitch(name) {
                    Level = GetSourceLevels(logger)
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(new LoggerTraceListener(name, logger));
            return source;
        }
#endif

        private void InitLogSource(string name)
        {
#if NETFRAMEWORK
            if (!TraceSources.TryGetValue(name, out m_TraceSource)) {
                m_TraceSource = new TraceSource(name);
                TraceSources.Add(name, m_TraceSource);
            }
#else
            if (!TraceSources.TryGetValue(name, out m_TraceSource)) {
                if (LoggerFactory != null) {
                    ILogger logger = LoggerFactory.CreateLogger(name);
                    if (logger != null) {
                        m_TraceSource = CreateFromLogger(name, logger);
                        Logger = logger;
                        Loggers.Add(name, logger);
                    } else {
                        m_TraceSource = new TraceSource(name);
                    }
                } else {
                    m_TraceSource = new TraceSource(name);
                }
                TraceSources.Add(name, m_TraceSource);
            } else {
                if (Loggers.TryGetValue(name, out ILogger cachedLogger)) {
                    Logger = cachedLogger;
                }
            }
#endif
        }

        /// <summary>
        /// Gets the trace source.
        /// </summary>
        /// <value>The trace source object.</value>
        /// <exception cref="ObjectDisposedException"/>
        public TraceSource TraceSource
        {
            get
            {
                if (m_TraceSource != null) return m_TraceSource;
                throw new ObjectDisposedException(m_Name);
            }
        }

#if NETSTANDARD
        /// <summary>
        /// If there is an <see cref="ILogger"/> associated with this instance, get it.
        /// </summary>
        [CLSCompliant(false)]
        public ILogger Logger { get; private set; }
#endif

        private static long GetTraceLevels(TraceSource source)
        {
            long levels = 0;
            if (source.Switch.ShouldTrace(TraceEventType.Critical)) levels |= (long)TraceEventType.Critical;
            if (source.Switch.ShouldTrace(TraceEventType.Error)) levels |= (long)TraceEventType.Error;
            if (source.Switch.ShouldTrace(TraceEventType.Warning)) levels |= (long)TraceEventType.Warning;
            if (source.Switch.ShouldTrace(TraceEventType.Information)) levels |= (long)TraceEventType.Information;
            if (source.Switch.ShouldTrace(TraceEventType.Verbose)) levels |= (long)TraceEventType.Verbose;
#if NETFRAMEWORK
            if (source.Switch.ShouldTrace(TraceEventType.Start)) levels |= (long)TraceEventType.Start;
            if (source.Switch.ShouldTrace(TraceEventType.Stop)) levels |= (long)TraceEventType.Stop;
            if (source.Switch.ShouldTrace(TraceEventType.Suspend)) levels |= (long)TraceEventType.Suspend;
            if (source.Switch.ShouldTrace(TraceEventType.Resume)) levels |= (long)TraceEventType.Resume;
            if (source.Switch.ShouldTrace(TraceEventType.Transfer)) levels |= (long)TraceEventType.Transfer;
#endif

            return levels;
        }

        /// <summary>
        /// Tests if tracing should be done for the log level provided.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <returns><see langword="true"/> if tracing is enabled; otherwise <see langword="false"/>.</returns>
        /// <remarks>Note, changing the trace source log level on the fly will not result in an update here.</remarks>
        public bool ShouldTrace(TraceEventType eventType)
        {
            return ((m_TraceLevels & (long)eventType) != 0);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">
        /// One of the enumeration values that specifies the event type of the trace data.
        /// </param>
        /// <param name="message">The trace message to write.</param>
        public void TraceEvent(TraceEventType eventType, string message)
        {
            TraceSource.TraceEvent(eventType, 0, message);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="format">
        /// A composite format string that contains text intermixed with zero or more format items, which correspond to
        /// objects in the <paramref name="args"/> array.
        /// </param>
        /// <param name="args">An object array containing zero or more objects to format.</param>
        public void TraceEvent(TraceEventType eventType, string format, params object[] args)
        {
            TraceSource.TraceEvent(eventType, 0, format, args);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(Exception e, string function, string message)
        {
            string logMessage;
            if (ShouldTrace(TraceEventType.Information)) {
                logMessage = string.Format("{0}: {1}{3}{2}", function, message, e, Environment.NewLine);
                TraceSource.TraceEvent(TraceEventType.Error, 0, logMessage);
            } else if (ShouldTrace(TraceEventType.Error)) {
                logMessage = string.Format("{0}: {1}{3}{2}", function, message, e.Message, Environment.NewLine);
                TraceSource.TraceEvent(TraceEventType.Error, 0, logMessage);
            }
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(Exception e, string function, string format, params object[] args)
        {
            TraceException(e, function, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(string name, Exception e, string function, string message)
        {
            string logMessage;
            if (ShouldTrace(TraceEventType.Information)) {
                logMessage = string.Format("{0}: {1}: {2}{4}{3}", name, function, message, e, Environment.NewLine);
                TraceSource.TraceEvent(TraceEventType.Error, 0, logMessage);
            } else if (ShouldTrace(TraceEventType.Error)) {
                logMessage = string.Format("{0}: {1}: {2}{4}{3}", name, function, message, e.Message, Environment.NewLine);
                TraceSource.TraceEvent(TraceEventType.Error, 0, logMessage);
            }
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(string name, Exception e, string function, string format, params object[] args)
        {
            TraceException(name, e, function, string.Format(format, args));
        }

        private bool m_IsDisposed;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting managed and unmanaged
        /// resources.
        /// </summary>
        public void Dispose()
        {
            if (m_IsDisposed) return;

            m_TraceSource.Flush();
            m_TraceSource.Close();
            m_TraceSource = null;
            m_IsDisposed = true;
        }
    }
}
