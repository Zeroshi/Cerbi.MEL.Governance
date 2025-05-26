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
                new Cerbi.Governance.FileGovernanceSource(settings.ConfigPath));

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider>(sp =>
            {
                var providers = sp.GetServices<ILoggerProvider>().ToList();
                var inner = providers.FirstOrDefault(p => p is not CerbiLoggerProvider);
                return new CerbiLoggerProvider(inner!, validator);
            }));

            return builder;
        }
    }
}
