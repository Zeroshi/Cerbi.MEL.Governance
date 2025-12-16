using Cerbi.Governance;                // for RuntimeGovernanceValidator, FileGovernanceSource
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Net.Http;

namespace Cerbi
{
    public static class CerbiLoggingBuilderExtensions
    {
        /// <summary>
        /// Adds Cerbi‐governance on top of a single Console sink.
        /// Call this instead of AddSimpleConsole(...) in your Program.cs.
        /// </summary>
        public static ILoggingBuilder AddCerbiGovernance(
            this ILoggingBuilder builder,
            Action<CerbiGovernanceMELSettings> configure
        )
        {
            var settings = new CerbiGovernanceMELSettings();
            configure(settings);

            var validator = new RuntimeGovernanceValidator(
                () => settings.Enabled && settings.EnforcementMode != GovernanceEnforcementMode.Off,
                settings.Profile,
                new FileGovernanceSource(settings.ConfigPath)
            );

            // Ensure a single ConsoleLoggerProvider can be resolved
            builder.Services.TryAddSingleton<ConsoleLoggerProvider>();

            // Register our provider that wraps the single Console sink
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
            {
                var consoleProv = sp.GetRequiredService<ConsoleLoggerProvider>();

                // Try to get IHttpClientFactory dynamically if available (no hard dependency)
                HttpClient client;
                var factoryType = Type.GetType("System.Net.Http.IHttpClientFactory, Microsoft.Extensions.Http");
                var factory = factoryType != null ? sp.GetService(factoryType) : null;
                if (factory != null)
                {
                    var method = factoryType!.GetMethod("CreateClient", new[] { typeof(string) });
                    client = (HttpClient)(method!.Invoke(factory, new object[] { "CerbiGovernance" })!);
                }
                else
                {
                    client = new HttpClient();
                }

                var queueSender = AzureServiceBusScoringSender.Create(settings.ScoringIngestion?.AzureServiceBus);
                var shipper = new ScoreShipper(client, settings.ScoreShipping, settings.ScoringIngestion, queueSender);

                return new CerbiLoggerProvider(
                    consoleProv,
                    validator,
                    settings.Profile,
                    settings,
                    shipper
                );
            });

            return builder;
        }

        /// <summary>
        /// Adds Cerbi governance with default settings.
        /// </summary>
        public static ILoggingBuilder AddCerbiGovernance(this ILoggingBuilder builder)
            => builder.AddCerbiGovernance(_ => { });

        /// <summary>
        /// Adds Cerbi governance, binding settings from configuration.
        /// Default section path: "Cerbi:Governance".
        /// </summary>
        public static ILoggingBuilder AddCerbiGovernance(
            this ILoggingBuilder builder,
            IConfiguration configuration,
            string sectionPath = "Cerbi:Governance")
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var section = configuration.GetSection(sectionPath);
            return builder.AddCerbiGovernance(opts =>
            {
                if (section == null) return;
                opts.Profile = section["Profile"] ?? opts.Profile;
                opts.ConfigPath = section["ConfigPath"] ?? opts.ConfigPath;
                if (bool.TryParse(section["Enabled"], out var enabled)) opts.Enabled = enabled;
                var mode = section["EnforcementMode"];
                if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse<GovernanceEnforcementMode>(mode, ignoreCase: true, out var parsed))
                    opts.EnforcementMode = parsed;
                var minLevel = section["MinValidationLevel"];
                if (!string.IsNullOrWhiteSpace(minLevel) && Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(minLevel, true, out var lvl))
                    opts.MinValidationLevel = lvl;
                if (double.TryParse(section["SamplingRate"], out var sr)) opts.SamplingRate = Math.Clamp(sr, 0.0, 1.0);

                opts.AppName = section["AppName"] ?? opts.AppName;
                opts.Environment = section["Environment"] ?? opts.Environment;

                var score = section.GetSection("ScoreShipping");
                if (score != null)
                {
                    if (bool.TryParse(score["Enabled"], out var se)) opts.ScoreShipping.Enabled = se;
                    if (bool.TryParse(score["LicenseAllowsScoring"], out var lic)) opts.ScoreShipping.LicenseAllowsScoring = lic;
                    if (int.TryParse(score["BatchSize"], out var bs)) opts.ScoreShipping.BatchSize = bs;
                    if (int.TryParse(score["MaxQueueSize"], out var mqs)) opts.ScoreShipping.MaxQueueSize = mqs;
                    if (int.TryParse(score["FlushIntervalSeconds"], out var fi)) opts.ScoreShipping.FlushIntervalSeconds = fi;
                    if (int.TryParse(score["MaxRetries"], out var mr)) opts.ScoreShipping.MaxRetries = mr;
                    if (int.TryParse(score["RetryDelayMilliseconds"], out var rd)) opts.ScoreShipping.RetryDelayMilliseconds = rd;
                    opts.ScoreShipping.Endpoint = score["Endpoint"] ?? opts.ScoreShipping.Endpoint;
                    opts.ScoreShipping.ApiKey = score["ApiKey"] ?? opts.ScoreShipping.ApiKey;
                }

                var ingestion = section.GetSection("ScoringIngestion");
                if (ingestion != null)
                {
                    var ingestionMode = ingestion["Mode"];
                    if (!string.IsNullOrWhiteSpace(ingestionMode) && Enum.TryParse<ScoringIngestionMode>(ingestionMode, true, out var ingestionParsed))
                        opts.ScoringIngestion.Mode = ingestionParsed;

                    var sb = ingestion.GetSection("AzureServiceBus");
                    if (sb != null)
                    {
                        opts.ScoringIngestion.AzureServiceBus.ConnectionString = sb["ConnectionString"] ?? opts.ScoringIngestion.AzureServiceBus.ConnectionString;
                        opts.ScoringIngestion.AzureServiceBus.QueueName = sb["QueueName"] ?? opts.ScoringIngestion.AzureServiceBus.QueueName;
                    }
                }
            });
        }
    }
}
