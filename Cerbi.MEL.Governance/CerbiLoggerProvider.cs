using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using System;

namespace Cerbi
{
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerProvider _inner;
        private readonly RuntimeGovernanceValidator _validator;

        public CerbiLoggerProvider(ILoggerProvider inner, RuntimeGovernanceValidator validator)
        {
            _inner = inner;
            _validator = validator;
        }

        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _inner.CreateLogger(categoryName);
            return new CerbiGovernanceLogger(innerLogger, _validator);
        }

        public void Dispose()
        {
            (_inner as IDisposable)?.Dispose();
        }
    }
}
