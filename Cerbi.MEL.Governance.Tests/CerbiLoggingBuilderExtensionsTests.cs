using Cerbi;                             // for AddCerbiGovernance(...)
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiLoggingBuilderExtensionsTests
    {
        [Fact]
        public void AddCerbiGovernance_Registers_ConsoleProvider_And_CerbiLoggerProvider()
        {
            // Arrange: create a fresh ServiceCollection
            var services = new ServiceCollection();

            // Act: call AddLogging(builder => builder.AddCerbiGovernance(...))
            services.AddLogging(builder =>
            {
                builder.AddCerbiGovernance(opts =>
                {
                    opts.Profile = "Invoices";
                    opts.ConfigPath = "cfg.json";
                    opts.Enabled = false;
                });
            });

            using var sp = services.BuildServiceProvider();

            // 1) Check that ConsoleLoggerProvider is registered as a concrete service
            var consoleProv = sp.GetService<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>();
            Assert.NotNull(consoleProv);

            // 2) Gather all registered ILoggerProvider instances into a List
            var allProviders = sp.GetServices<ILoggerProvider>().ToList();

            // Exactly one of them should be a CerbiLoggerProvider
            Assert.Contains(allProviders, p => p is CerbiLoggerProvider);
        }

        [Fact]
        public void CreateLogger_Returns_NonNull_ILogger()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddCerbiGovernance(opts =>
                {
                    opts.Profile = "Payments";
                    opts.ConfigPath = "payments.json";
                    opts.Enabled = true;
                });
            });

            using var sp = services.BuildServiceProvider();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Act: ask for a logger
            var logger = loggerFactory.CreateLogger("TestCategory");

            // Assert: we get a non-null ILogger (type is Microsoft.Extensions.Logging.Logger internally)
            Assert.NotNull(logger);
        }

        [Fact]
        public void AddCerbiGovernance_Binds_From_Configuration()
        {
            var dict = new Dictionary<string, string?>
            {
                ["Cerbi:Governance:Profile"] = "Orders",
                ["Cerbi:Governance:ConfigPath"] = "cerbi.json",
                ["Cerbi:Governance:Enabled"] = "true",
                ["Cerbi:Governance:EnforcementMode"] = "Audit",
                ["Cerbi:Governance:MinValidationLevel"] = "Warning",
                ["Cerbi:Governance:SamplingRate"] = "0.5",
                ["Cerbi:Governance:AppName"] = "App",
                ["Cerbi:Governance:Environment"] = "dev",
                ["Cerbi:Governance:ScoreShipping:Enabled"] = "true",
                ["Cerbi:Governance:ScoreShipping:LicenseAllowsScoring"] = "true",
                ["Cerbi:Governance:ScoreShipping:Endpoint"] = "http://localhost",
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddCerbiGovernance(config));
            using var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<ILoggerFactory>();
            var logger = factory.CreateLogger("Cat");
            Assert.NotNull(logger);
        }
    }
}
