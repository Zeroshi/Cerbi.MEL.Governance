using Microsoft.Extensions.Logging;
using Cerbi;
using System;
using System.Collections.Generic;
using System.Linq;
using Cerbi.Governance;

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

        public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = ExtractFields(state);
            var validated = _validator.Validate(fields);

            if (validated.TryGetValue("GovernanceViolations", out var v) && v is IEnumerable<string> violations && violations.Any())
            {
                // Block log or tag it; here we block
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
    }

}
