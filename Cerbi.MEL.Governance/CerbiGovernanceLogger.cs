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

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);


        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ExtractFields(state);
            var callerType = GetCallerType();

            var topic = GetTopicOverride();

            if (!string.IsNullOrWhiteSpace(topic))
                fields["CerbiTopic"] = topic;

            var validated = _validator.Validate(fields);


            if (validated.TryGetValue("GovernanceViolations", out var v) &&
                v is IEnumerable<string> violations &&
                violations.Any())
            {
                return;
            }

            _inner.Log(logLevel, eventId, state, exception, formatter);
        }


        private Dictionary<string, object> ExtractFields<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
                return kvps.ToDictionary(k => k.Key, v => v.Value);

            return new Dictionary<string, object> { { "Message", state?.ToString() ?? "" } };
        }

        private static string? GetTopicOverride()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames())
            {
                var method = frame?.GetMethod();
                var declaringType = method?.DeclaringType;

                if (declaringType == null || declaringType.FullName?.StartsWith("Microsoft.Extensions") == true)
                    continue;

                var attr = declaringType.GetCustomAttributes(typeof(CerbiTopicAttribute), true)
                    .FirstOrDefault() as CerbiTopicAttribute;

                if (attr != null)
                    return attr.TopicName;
            }

            return null;
        }


        private static Type? GetCallerType()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames())
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (type == null || type.FullName?.StartsWith("Microsoft.Extensions") == true)
                    continue;

                // skip internal Cerbi loggers
                if (type.Assembly.FullName?.Contains("Cerbi") != true)
                    return type;
            }

            return null;
        }

    }

}
