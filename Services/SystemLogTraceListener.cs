using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BIS.ERP.Services
{
    public sealed class SystemLogTraceListener : TraceListener
    {
        private readonly object _syncRoot = new();
        private readonly StringBuilder _lineBuffer = new();
        private readonly string _logSource;

        public SystemLogTraceListener(string logSource)
        {
            _logSource = logSource;
        }

        public override void Write(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            lock (_syncRoot)
            {
                _lineBuffer.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            var fullMessage = BuildBufferedMessage(message);
            WriteTraceMessage(TraceEventType.Warning, fullMessage);
        }

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            WriteTraceMessage(eventType, $"{source}: {message}");
        }

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? format,
            params object?[]? args)
        {
            var message = string.IsNullOrEmpty(format)
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, format, args ?? []);

            TraceEvent(eventCache, source, eventType, id, message);
        }

        private string BuildBufferedMessage(string? message)
        {
            lock (_syncRoot)
            {
                if (_lineBuffer.Length == 0)
                {
                    return message ?? string.Empty;
                }

                _lineBuffer.Append(message);
                var fullMessage = _lineBuffer.ToString();
                _lineBuffer.Clear();
                return fullMessage;
            }
        }

        private void WriteTraceMessage(TraceEventType eventType, string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var normalizedMessage = message.Trim();

            if (eventType is TraceEventType.Critical or TraceEventType.Error ||
                normalizedMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("ошибка", StringComparison.OrdinalIgnoreCase))
            {
                SystemLogService.Error(normalizedMessage, _logSource);
                return;
            }

            SystemLogService.Warning(normalizedMessage, _logSource);
        }
    }
}
