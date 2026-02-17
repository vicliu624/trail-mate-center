// According to https://github.com/dotnet/roslyn-analyzers/issues/5626, the main purpose for this diagnostic is: 'the
// point of this analyzer is to determine whether the code is building a string to use in the logger.' We do but that is
// because we're also a logger.

#pragma warning disable CA2254 // Template should be a static expression.

namespace RJCP.Diagnostics.Trace
{
    using System.Diagnostics;
    using Internal;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A <see cref="TraceListener"/> wrapper for a .NET Core <see cref="ILogger"/>.
    /// </summary>
    /// <remarks>
    /// This class is not intended to be instantiated within a <c>app.config</c> file. It is provided so that a user of
    /// .NET Core can provide an <see cref="ILogger"/> object, and the code in this library can continue to use the
    /// <see cref="TraceSource"/> classes for logging.
    /// </remarks>
    internal class LoggerTraceListener : TraceListener
    {
        private readonly ILogger m_Logger;
        private readonly LineSplitter m_Lines = new LineSplitter();

        public LoggerTraceListener(string name, ILogger logger) : base(name)
        {
            m_Logger = logger;
        }

        public ILogger Logger { get { return m_Logger; } }

        public override void Fail(string message, string detailMessage)
        {
            if (m_Lines.IsCached) m_Lines.NewLine();

            string logMessage;
            if (detailMessage == null) {
                logMessage = message;
            } else {
                logMessage = string.Format("{0}: {1}", message, detailMessage);
            }
            m_Lines.AppendLine(logMessage);

            foreach (string line in m_Lines) {
                m_Logger.LogInformation(line);
            }
        }

        public override void Write(string message)
        {
            m_Lines.Append(message);
            foreach (string line in m_Lines) {
                m_Logger.LogInformation(line);
            }
        }

        public override void WriteLine(string message)
        {
            m_Lines.AppendLine(message);
            foreach (string line in m_Lines) {
                m_Logger.LogInformation(line);
            }
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id)
        {
            TraceEvent(eventCache, source, eventType, id, string.Empty);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            string message = string.Format(format, args);
            TraceEvent(eventCache, source, eventType, id, message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            Flush();

            switch (eventType) {
            case TraceEventType.Critical:
                m_Logger.LogCritical(id, message);
                break;
            case TraceEventType.Error:
                m_Logger.LogError(id, message);
                break;
            case TraceEventType.Warning:
                m_Logger.LogWarning(id, message);
                break;
            case TraceEventType.Information:
                m_Logger.LogInformation(id, message);
                break;
            case TraceEventType.Verbose:
                m_Logger.LogDebug(id, message);
                break;
            default:
                m_Logger.LogTrace(id, message);
                break;
            }
        }

        public override void Flush()
        {
            if (m_Lines.IsCached) m_Lines.NewLine();
            foreach (string line in m_Lines) {
                m_Logger.LogInformation(line);
            }
        }
    }
}
