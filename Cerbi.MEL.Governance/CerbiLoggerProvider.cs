using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Net.Http;

namespace Cerbi
{
    /// <summary>
    /// This provider wraps a ConsoleLoggerProvider (the "real" console sink)
    /// and injects CerbiGovernanceLogger on top of it.
    /// Owns a ScoreShipper for governance score events.
    /// </summary>
    public class CerbiLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConsoleLoggerProvider _consoleProvider;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _defaultTopic;
        private readonly IScoreShipper _scoreShipper;
        private readonly CerbiGovernanceMELSettings _settings;
        private IExternalScopeProvider? _scopeProvider;

        // Legacy constructor kept for backward compatibility (tests etc.)
        public CerbiLoggerProvider(ConsoleLoggerProvider consoleProvider, RuntimeGovernanceValidator validator, string profileName)
            : this(consoleProvider, validator, profileName, new CerbiGovernanceMELSettings(), new ScoreShipper(new HttpClient(), new ScoreShippingOptions())) { }

        public CerbiLoggerProvider(
            ConsoleLoggerProvider consoleProvider,
            RuntimeGovernanceValidator validator,
            string profileName,
            CerbiGovernanceMELSettings settings,
            IScoreShipper scoreShipper)
        {
            _consoleProvider = consoleProvider;
            _validator = validator;
            _defaultTopic = profileName ?? string.Empty;
            _settings = settings;
            _scoreShipper = scoreShipper;
        }

        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _consoleProvider.CreateLogger(categoryName);
            if (_scopeProvider is not null && innerLogger is ISupportExternalScope supports)
            {
                supports.SetScopeProvider(_scopeProvider);
            }
            return new CerbiGovernanceLogger(innerLogger, _validator, _defaultTopic, categoryName, () => _settings.Enabled, _scoreShipper, _settings);
        }

        public void Dispose()
        {
            _consoleProvider.Dispose();
            _scoreShipper.Dispose();
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}
