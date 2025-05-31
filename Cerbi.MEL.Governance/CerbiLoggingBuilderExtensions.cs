using Cerbi.Governance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace Cerbi
{
    public static class CerbiLoggingBuilderExtensions
    {
        public static ILoggingBuilder AddCerbiGovernance(
            this ILoggingBuilder builder,
            Action<CerbiGovernanceMELSettings> configure)
        {
            // 1) Let the caller configure profile/path/enabled
            var settings = new CerbiGovernanceMELSettings();
            configure(settings);

            // 2) Build your RuntimeGovernanceValidator once
            var validator = new RuntimeGovernanceValidator(
                () => settings.Enabled,
                settings.Profile,
                new FileGovernanceSource(settings.ConfigPath)
            );

            // 3) Ensure a ConsoleLoggerProvider is registered first (if not already)
            //    This guarantees that `ConsoleLoggerProvider` can be injected below.
            builder.Services.AddSingleton<ConsoleLoggerProvider>();

            // 4) Register CerbiLoggerProvider, wrapping the console provider
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
            {
                // Resolve the ConsoleLoggerProvider from DI:
                var consoleProv = sp.GetRequiredService<ConsoleLoggerProvider>();
                return new CerbiLoggerProvider(consoleProv, validator, settings.Profile);
            });

            return builder;
        }
    }
}
