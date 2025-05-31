using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Cerbi.Governance;

namespace Cerbi
{
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ConsoleLoggerProvider _consoleProvider;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _profileName;

        public CerbiLoggerProvider(
            ConsoleLoggerProvider consoleProvider,
            RuntimeGovernanceValidator validator,
            string profileName)
        {
            _consoleProvider = consoleProvider;
            _validator = validator;
            _profileName = profileName;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // wrap just the console logger
            var inner = _consoleProvider.CreateLogger(categoryName);
            return new CerbiGovernanceLogger(inner, _validator, _profileName);
        }

        public void Dispose()
        {
            _consoleProvider.Dispose();
        }
    }
}
