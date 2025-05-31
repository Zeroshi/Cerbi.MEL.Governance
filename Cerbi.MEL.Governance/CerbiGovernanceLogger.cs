using Cerbi.Governance;
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
        private readonly string _profileName;

        public CerbiGovernanceLogger(
            ILogger inner,
            RuntimeGovernanceValidator validator,
            string profileName)
        {
            _inner = inner;
            _validator = validator;
            _profileName = profileName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _inner.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // 1) Attempt to find [CerbiTopic("...")] on the call stack
            var topic = TryResolveTopic();

            // 2) If no topic is found, bypass Cerbi and log exactly once
            if (string.IsNullOrWhiteSpace(topic))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            // 3) Extract structured fields out of TState
            var fields = ExtractFields(state);

            // 4) Inject the CerbiTopic so our validator knows which profile to use
            fields["CerbiTopic"] = topic!;

            // 5) Run the governance validation
            var validated = _validator.Validate(fields);

            // 6) If there are violations, inject them. Otherwise record that we enforced with the chosen profile.
            if (validated.TryGetValue("GovernanceViolations", out var v)
                && v is IEnumerable<string> violations
                && violations.Any())
            {
                fields["GovernanceViolations"] = violations.ToArray();
                fields["GovernanceRelaxed"] = false;
                fields["GovernanceProfileUsed"] = _profileName;
            }
            else
            {
                // No violations: still record that we enforced and which profile
                fields["GovernanceProfileUsed"] = _profileName;
                fields["GovernanceEnforced"] = true;
                fields["GovernanceMode"] = "Strict";
                // If you want “Relaxed” mode instead, swap the string here accordingly.
            }

            // 7) Build a single combined message:
            //    [Cerbi] <original‐formatted‐message> | <all‐fields‐JSON>
            string originalText = formatter(state, exception);
            string governanceJson = JsonSerializer.Serialize(fields);

            string mergedMessage = $"[Cerbi] {originalText} | {governanceJson}";

            // 8) Finally, log only once to the inner provider, using the mergedMessage as the “state”
            _inner.Log(
                logLevel,
                eventId,
                mergedMessage,
                exception,
                (msg, ex) => msg!);
        }

        // Walk the stack to find the first non‐Microsoft.Extensions type with [CerbiTopic("...")]
        private static string? TryResolveTopic()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                var declaring = method?.DeclaringType;
                var fullName = declaring?.FullName;

                // Skip any Microsoft.Extensions frames entirely
                if (string.IsNullOrWhiteSpace(fullName)
                    || fullName.StartsWith("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Look for our custom attribute:
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

        // If TState is IEnumerable<KeyValuePair<string,object>>, we copy into a dictionary.
        // Otherwise, we just emit one field named "Message" = state.ToString().
        private static Dictionary<string, object> ExtractFields<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                return kvps.ToDictionary(k => k.Key, v => v.Value);
            }

            return new Dictionary<string, object>
            {
                { "Message", state?.ToString() ?? string.Empty }
            };
        }
    }
}
