using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Cerbi.Governance;

namespace Cerbi
{
    public static class CerbiLoggingBuilderExtensions
    {
        public static ILoggingBuilder AddCerbiGovernance(this ILoggingBuilder builder, Action<CerbiGovernanceMELSettings> configure)
        {
            var settings = new CerbiGovernanceMELSettings();
            configure(settings);

            var validator = new RuntimeGovernanceValidator(
                () => settings.Enabled,
                settings.Profile,
                new FileGovernanceSource(settings.ConfigPath)
            );

            builder.Services.TryAddSingleton<ILoggerProvider>(sp =>
            {
                var factory = sp.GetRequiredService<ILoggerFactory>();
                return new CerbiLoggerProvider(factory, validator, settings.Profile);
            });

            return builder;
        }
    }
}
