using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Linq;
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

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider>(sp =>
            {
                var existing = sp.GetServices<ILoggerProvider>().Where(p => p is not CerbiLoggerProvider).ToList();
                var composite = new CompositeLoggerProvider(existing);
                return new CerbiLoggerProvider(composite, validator);
            }));

            return builder;
        }
    }
}
