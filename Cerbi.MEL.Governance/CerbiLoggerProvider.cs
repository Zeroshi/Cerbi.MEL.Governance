using Cerbi.Governance;                // for RuntimeGovernanceValidator
using Microsoft.Extensions.Logging;

namespace Cerbi
{
    /// <summary>
    /// This provider wraps the host’s ILoggerFactory and injects a CerbiGovernanceLogger on top.
    /// </summary>
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerFactory _innerFactory;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _profileName;

        public CerbiLoggerProvider(
            ILoggerFactory innerFactory,
            RuntimeGovernanceValidator validator,
            string profileName)
        {
            _innerFactory = innerFactory;
            _validator = validator;
            _profileName = profileName;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // Ask the existing ILoggerFactory to produce a “real” ILogger (e.g. Console sink, etc.)
            var innerLogger = _innerFactory.CreateLogger(categoryName);
            return new CerbiGovernanceLogger(innerLogger, _validator, _profileName);
        }

        public void Dispose()
        {
            // We do NOT dispose the innerFactory here.  The host will tear it down at shutdown.
        }
    }
}
