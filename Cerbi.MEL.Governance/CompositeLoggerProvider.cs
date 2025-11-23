using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cerbi
{
    public class CompositeLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly IEnumerable<ILoggerProvider> _providers;
        private IExternalScopeProvider? _scopeProvider;

        public CompositeLoggerProvider(IEnumerable<ILoggerProvider> providers)
        {
            _providers = providers;
        }

        public ILogger CreateLogger(string categoryName)
        {
            var loggers = _providers.Select(p => p.CreateLogger(categoryName)).ToList();
            foreach (var logger in loggers)
            {
                if (_scopeProvider != null && logger is ISupportExternalScope s)
                {
                    s.SetScopeProvider(_scopeProvider);
                }
            }
            return new CompositeLogger(loggers);
        }

        public void Dispose()
        {
            foreach (var provider in _providers)
                (provider as IDisposable)?.Dispose();
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
            foreach (var p in _providers)
            {
                if (p is ISupportExternalScope s)
                {
                    s.SetScopeProvider(scopeProvider);
                }
            }
        }

        private class CompositeLogger : ILogger
        {
            private readonly List<ILogger> _loggers;

            public CompositeLogger(List<ILogger> loggers)
            {
                _loggers = loggers;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                var scopes = _loggers.Select(l => l.BeginScope(state)).ToList();
                return new CompositeScope(scopes);
            }

            public bool IsEnabled(LogLevel logLevel) =>
                _loggers.Any(l => l.IsEnabled(logLevel));

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                foreach (var logger in _loggers)
                {
                    logger.Log(logLevel, eventId, state, exception, formatter);
                }
            }

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
