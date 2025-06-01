using Cerbi.Governance;                  // for RuntimeGovernanceValidator, FileGovernanceSource
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Cerbi
{
    public static class CerbiLoggingBuilderExtensions
    {
        /// <summary>
        /// Call this immediately after you do AddSimpleConsole(...).
        /// It wraps the host’s existing ILoggerFactory (which already has a Console sink)
        /// so that every ILogger<T> becomes a CerbiGovernanceLogger.
        /// </summary>
        public static ILoggingBuilder AddCerbiGovernance(
            this ILoggingBuilder builder,
            Action<CerbiGovernanceMELSettings> configure)
        {
            // 1) Let the caller configure Profile, ConfigPath, Enabled
            var settings = new CerbiGovernanceMELSettings();
            configure(settings);

            // 2) Build one RuntimeGovernanceValidator that all loggers will share:
            var validator = new RuntimeGovernanceValidator(
                () => settings.Enabled,
                settings.Profile,
                new FileGovernanceSource(settings.ConfigPath)
            );

            //
            // 3) We assume the caller already did:
            //      logging.AddSimpleConsole(...)
            //    so that the host’s ILoggerFactory has a Console sink inside it.
            //

            // 4) Register our CerbiLoggerProvider, wrapping the existing ILoggerFactory:
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
            {
                // Grab the host’s ILoggerFactory (already containing Console sink, etc.)
                var factory = sp.GetRequiredService<ILoggerFactory>();

                return new CerbiLoggerProvider(
                    factory,
                    validator,
                    settings.Profile
                );
            });

            return builder;
        }
    }
}
