using Cerbi.Governance;    // for RuntimeGovernanceValidator, FileGovernanceSource
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Cerbi
{
    public class CerbiGovernanceLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _defaultTopic;

        public CerbiGovernanceLogger(
            ILogger inner,
            RuntimeGovernanceValidator validator,
            string defaultTopic)
        {
            _inner = inner;
            _validator = validator;
            _defaultTopic = defaultTopic ?? string.Empty;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // 1) Determine Cerbi topic (attribute‐based or fallback):
            var topicFromAttribute = TryResolveTopic();
            var topic = !string.IsNullOrWhiteSpace(topicFromAttribute)
                        ? topicFromAttribute!
                        : _defaultTopic;

            // 2) If no topic at all, just delegate directly—no Cerbi enrichment:
            if (string.IsNullOrWhiteSpace(topic))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            // 3) Extract structured fields from the “state” if possible:
            var fields = ExtractFields(state);

            // 4) Inject the “CerbiTopic” so the validator knows which profile to use:
            fields["CerbiTopic"] = topic;

            // 5) Run governance‐validation:
            var validated = _validator.Validate(fields);

            // 6) If there are violations, record them; otherwise record “enforced”:
            if (validated.TryGetValue("GovernanceViolations", out var v)
                && v is IEnumerable<string> violations
                && violations.Any())
            {
                fields["GovernanceViolations"] = violations.ToArray();
                fields["GovernanceRelaxed"] = false;
                fields["GovernanceProfileUsed"] = topic;
            }
            else
            {
                fields["GovernanceProfileUsed"] = topic;
                fields["GovernanceEnforced"] = true;
                fields["GovernanceMode"] = "Strict";
            }

            // ────────────────────────────────────────────────────────────────────
            // 7a) First, log the original message exactly as the caller wrote it:
            // ────────────────────────────────────────────────────────────────────
            _inner.Log(logLevel, eventId, state, exception, formatter);

            // ────────────────────────────────────────────────────────────────────
            // 7b) Then serialize our “fields” dictionary to JSON and log it:
            // ────────────────────────────────────────────────────────────────────
            string jsonPayload = JsonSerializer.Serialize(fields);
            _inner.Log(
                logLevel,      // same severity
                eventId,       // same EventId
                jsonPayload,   // pass the JSON string as the “state”
                exception,
                (msg, ex) => msg!    // simple formatter: just prints the JSON string
            );
        }

        /// <summary>
        /// Walks the stack to find the first non‐Microsoft.Extensions class (or method)
        /// decorated with [CerbiTopic("…")]. Returns that topic or null if none.
        /// </summary>
        private static string? TryResolveTopic()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var declaring = frame.GetMethod()?.DeclaringType;
                if (declaring == null)
                    continue;

                var fullName = declaring.FullName;
                // Skip any Microsoft.Extensions.* frames:
                if (string.IsNullOrWhiteSpace(fullName)
                    || fullName.StartsWith("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Look for our CerbiTopicAttribute on that type:
                var attr = declaring
                    .GetCustomAttributes(typeof(CerbiTopicAttribute), inherit: true)
                    .FirstOrDefault() as CerbiTopicAttribute;
                if (attr != null)
                {
                    return attr.TopicName;
                }
            }

            return null;
        }

        /// <summary>
        /// If TState is IEnumerable<KeyValuePair<string,object>>, copy into a Dictionary.
        /// Otherwise, return a new Dictionary { "Message": state.ToString() }.
        /// </summary>
        private static Dictionary<string, object> ExtractFields<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                return kvps.ToDictionary(k => k.Key, v => v.Value!);
            }

            return new Dictionary<string, object>
            {
                { "Message", state?.ToString() ?? string.Empty }
            };
        }
    }
}
