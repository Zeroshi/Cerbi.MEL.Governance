using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Net.Http;

namespace Cerbi
{
    /// <summary>
    /// This provider wraps a ConsoleLoggerProvider (the “real” console sink)
    /// and injects CerbiGovernanceLogger on top of it.
    /// Owns a ScoreShipper for governance score events.
    /// </summary>
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ConsoleLoggerProvider _consoleProvider;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _defaultTopic;
        private readonly ScoreShipper _scoreShipper;
        private readonly CerbiGovernanceMELSettings _settings;

        // Legacy constructor kept for backward compatibility (tests etc.)
        public CerbiLoggerProvider(ConsoleLoggerProvider consoleProvider, RuntimeGovernanceValidator validator, string profileName)
            : this(consoleProvider, validator, profileName, new CerbiGovernanceMELSettings()) { }

        public CerbiLoggerProvider(
            ConsoleLoggerProvider consoleProvider,
            RuntimeGovernanceValidator validator,
            string profileName,
            CerbiGovernanceMELSettings settings)
        {
            _consoleProvider = consoleProvider;
            _validator = validator;
            _defaultTopic = profileName ?? string.Empty;
            _settings = settings;
            // Create shipper (will be inert if disabled)
            _scoreShipper = new ScoreShipper(new HttpClient(), _settings.ScoreShipping);
        }

        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _consoleProvider.CreateLogger(categoryName);
            return new CerbiGovernanceLogger(innerLogger, _validator, _defaultTopic, categoryName, () => _settings.Enabled, _scoreShipper, _settings);
        }

        public void Dispose()
        {
            _consoleProvider.Dispose();
            _scoreShipper.Dispose();
        }
    }
}
