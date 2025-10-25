using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Reflection;

namespace Cerbi
{
    /// <summary>
    /// This provider wraps a ConsoleLoggerProvider (the “real” console sink)
    /// and injects CerbiGovernanceLogger on top of it.
    /// </summary>
    public class CerbiLoggerProvider : ILoggerProvider
    {
        private readonly ConsoleLoggerProvider _consoleProvider;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _defaultTopic;

        public CerbiLoggerProvider(
            ConsoleLoggerProvider consoleProvider,
            RuntimeGovernanceValidator validator,
            string profileName)
        {
            _consoleProvider = consoleProvider;
            _validator = validator;
            _defaultTopic = profileName ?? string.Empty;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // Ask the “real” console sink to create its ILogger for this category:
            var innerLogger = _consoleProvider.CreateLogger(categoryName);

            // Try to resolve a topic from the category type name (if it's a type full name)
            string? categoryTopic = null;
            var categoryType = Type.GetType(categoryName, throwOnError: false);
            if (categoryType != null)
            {
                var attr = categoryType.GetCustomAttribute<CerbiTopicAttribute>(inherit: true);
                if (attr != null) categoryTopic = attr.TopicName;
            }

            // Wrap that console‐logger in your CerbiGovernanceLogger and pass category Topic
            return new CerbiGovernanceLogger(innerLogger, _validator, _defaultTopic, categoryTopic);
        }

        public void Dispose()
        {
            // Only dispose the console sink when the host shuts down:
            _consoleProvider.Dispose();
        }
    }
}
