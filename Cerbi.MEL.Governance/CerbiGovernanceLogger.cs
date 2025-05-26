using Cerbi;
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Cerbi
{
    public class CerbiGovernanceLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly RuntimeGovernanceValidator _validator;

        public CerbiGovernanceLogger(ILogger inner, RuntimeGovernanceValidator validator)
        {
            _inner = inner;
            _validator = validator;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ExtractFields(state);

            // Inject CerbiTopic if decorated on the caller
            var topic = TryResolveTopic();
            if (!string.IsNullOrWhiteSpace(topic))
                fields["CerbiTopic"] = topic;

            var validated = _validator.Validate(fields);

            if (validated.TryGetValue("GovernanceViolations", out var v) &&
                v is IEnumerable<string> violations &&
                violations.Any())
            {
                return; // Block the log
            }

            _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private Dictionary<string, object> ExtractFields<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
                return kvps.ToDictionary(k => k.Key, v => v.Value);

            return new Dictionary<string, object> { { "Message", state?.ToString() ?? "" } };
        }

        private static string? TryResolveTopic()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var declaringType = frame.GetMethod()?.DeclaringType;
                if (declaringType == null || declaringType.FullName?.StartsWith("Microsoft.Extensions") == true)
                    continue;

                var attr = declaringType
                    .GetCustomAttributes(typeof(CerbiTopicAttribute), inherit: true)
                    .FirstOrDefault() as CerbiTopicAttribute;

                if (attr != null)
                    return attr.TopicName;
            }

            return null;
        }
    }
}
