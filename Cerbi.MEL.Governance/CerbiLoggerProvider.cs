using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using System;

namespace Cerbi
{
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerFactory _factory;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _profileName;

        public CerbiLoggerProvider(ILoggerFactory factory, RuntimeGovernanceValidator validator, string profileName)
        {
            _factory = factory;
            _validator = validator;
            _profileName = profileName;
        }

        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _factory.CreateLogger(categoryName);
            return new CerbiGovernanceLogger(innerLogger, _validator, _profileName);
        }

        public void Dispose()
        {
            (_factory as IDisposable)?.Dispose();
        }
    }
}
