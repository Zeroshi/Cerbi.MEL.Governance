using Cerbi.Governance;                // for RuntimeGovernanceValidator, FileGovernanceSource
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace Cerbi
{
    public static class CerbiLoggingBuilderExtensions
    {
        /// <summary>
        /// Adds Cerbi‐governance on top of a single Console sink.  
        /// Call this *instead* of AddSimpleConsole(...) in your Program.cs.
        /// </summary>
        public static ILoggingBuilder AddCerbiGovernance(
            this ILoggingBuilder builder,
            Action<CerbiGovernanceMELSettings> configure
        )
        {
            // 1) Let the caller configure Profile, ConfigPath, Enabled
            var settings = new CerbiGovernanceMELSettings();
            configure(settings);

            // 2) Build one RuntimeGovernanceValidator (shared by all loggers)
            var validator = new RuntimeGovernanceValidator(
                () => settings.Enabled,
                settings.Profile,
                new FileGovernanceSource(settings.ConfigPath)
            );

            //
            // 3) Register exactly one ConsoleLoggerProvider in DI.
            //    This guarantees that `ConsoleLoggerProvider` can be resolved below.
            //
            builder.Services.AddSingleton<ConsoleLoggerProvider>();

            //
            // 4) Register our CerbiLoggerProvider, wrapping the single ConsoleLoggerProvider:
            //
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
            {
                // a. Resolve the one-and-only ConsoleLoggerProvider
                var consoleProv = sp.GetRequiredService<ConsoleLoggerProvider>();

                // b. Wrap it in our CerbiLoggerProvider
                return new CerbiLoggerProvider(
                    consoleProv,     // the one Console sink
                    validator,       // shared validator
                    settings.Profile // fallback profile name
                );
            });

            return builder;
        }
    }
}
