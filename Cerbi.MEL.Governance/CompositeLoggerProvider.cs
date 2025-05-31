using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cerbi
{
    // This is a public type at namespace scope
    public class CompositeLoggerProvider : ILoggerProvider
    {
        private readonly IEnumerable<ILoggerProvider> _providers;

        public CompositeLoggerProvider(IEnumerable<ILoggerProvider> providers)
        {
            _providers = providers;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // Ask each inner provider to create its own ILogger
            var loggers = _providers.Select(p => p.CreateLogger(categoryName)).ToList();
            return new CompositeLogger(loggers);
        }

        public void Dispose()
        {
            // Dispose each inner provider (if it implements IDisposable)
            foreach (var provider in _providers)
                (provider as IDisposable)?.Dispose();
        }

        // This nested class is private—**but it is inside CompositeLoggerProvider**, not at namespace scope
        private class CompositeLogger : ILogger
        {
            private readonly List<ILogger> _loggers;

            public CompositeLogger(List<ILogger> loggers)
            {
                _loggers = loggers;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                // Begin a scope on each inner logger; wrap them in CompositeScope
                var scopes = _loggers.Select(l => l.BeginScope(state)).ToList();
                return new CompositeScope(scopes);
            }

            public bool IsEnabled(LogLevel logLevel) =>
                // Return true if ANY inner logger is enabled at this level
                _loggers.Any(l => l.IsEnabled(logLevel));

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                // Fan out the same log call to every inner logger
                foreach (var logger in _loggers)
                {
                    logger.Log(logLevel, eventId, state, exception, formatter);
                }
            }

            // This nested class is private too—but it is inside CompositeLogger, not at namespace scope
            private class CompositeScope : IDisposable
            {
                private readonly List<IDisposable> _scopes;

                public CompositeScope(List<IDisposable> scopes)
                {
                    _scopes = scopes;
                }

                public void Dispose()
                {
                    foreach (var scope in _scopes)
                        scope.Dispose();
                }
            }
        }
    }
}
