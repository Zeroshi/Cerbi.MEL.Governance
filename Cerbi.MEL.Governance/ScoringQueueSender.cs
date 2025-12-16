using Cerbi.Contracts;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cerbi
{
    internal interface IScoringQueueSender : IDisposable
    {
        bool IsConfigured { get; }
        Task SendAsync(ScoringQueueEnvelopeDto envelope, CancellationToken cancellationToken);
    }

    internal sealed class NoopScoringQueueSender : IScoringQueueSender
    {
        public static readonly NoopScoringQueueSender Instance = new();
        public bool IsConfigured => false;
        public Task SendAsync(ScoringQueueEnvelopeDto envelope, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class AzureServiceBusScoringSender : IScoringQueueSender
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ServiceBusConnectionInfo _connectionInfo;

        private AzureServiceBusScoringSender(ServiceBusConnectionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
            _httpClient = new HttpClient
            {
                BaseAddress = connectionInfo.BaseUri
            };
        }

        public static IScoringQueueSender Create(AzureServiceBusOptions? options)
        {
            if (options == null) return NoopScoringQueueSender.Instance;
            if (string.IsNullOrWhiteSpace(options.ConnectionString) || string.IsNullOrWhiteSpace(options.QueueName))
                return NoopScoringQueueSender.Instance;

            try
            {
                var info = ServiceBusConnectionInfo.Parse(options.ConnectionString, options.QueueName);
                return new AzureServiceBusScoringSender(info);
            }
            catch
            {
                return NoopScoringQueueSender.Instance;
            }
        }

        public bool IsConfigured => true;

        public async Task SendAsync(ScoringQueueEnvelopeDto envelope, CancellationToken cancellationToken)
        {
            var relativeUri = new Uri($"{_connectionInfo.QueueName.Trim('/')}/messages", UriKind.Relative);
            using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri);
            request.Headers.TryAddWithoutValidation("Authorization", _connectionInfo.CreateSasToken(relativeUri));

            var brokerProperties = new Dictionary<string, string?>
            {
                ["MessageId"] = envelope.IdempotencyKey,
            };
            if (!string.IsNullOrWhiteSpace(envelope.CorrelationId))
            {
                brokerProperties["CorrelationId"] = envelope.CorrelationId;
            }
            request.Headers.TryAddWithoutValidation("BrokerProperties", JsonSerializer.Serialize(brokerProperties));

            var payload = JsonSerializer.Serialize(envelope, SerializerOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private sealed class ServiceBusConnectionInfo
        {
            public Uri BaseUri { get; }
            public string QueueName { get; }
            private readonly string _sharedAccessKeyName;
            private readonly byte[] _sharedAccessKey;

            private ServiceBusConnectionInfo(Uri baseUri, string queueName, string keyName, byte[] key)
            {
                BaseUri = baseUri;
                QueueName = queueName;
                _sharedAccessKeyName = keyName;
                _sharedAccessKey = key;
            }

            public static ServiceBusConnectionInfo Parse(string connectionString, string queueName)
            {
                var pairs = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in pairs)
                {
                    var idx = pair.IndexOf('=');
                    if (idx <= 0) continue;
                    var tokenKey = pair.Substring(0, idx);
                    var value = pair[(idx + 1)..];
                    dict[tokenKey] = value;
                }

                if (!dict.TryGetValue("Endpoint", out var endpoint))
                    throw new InvalidOperationException("Service Bus connection string missing Endpoint.");
                if (!dict.TryGetValue("SharedAccessKeyName", out var keyName))
                    throw new InvalidOperationException("Service Bus connection string missing SharedAccessKeyName.");
                if (!dict.TryGetValue("SharedAccessKey", out var sharedKey))
                    throw new InvalidOperationException("Service Bus connection string missing SharedAccessKey.");
                if (string.IsNullOrWhiteSpace(queueName) && dict.TryGetValue("EntityPath", out var entityPath))
                    queueName = entityPath;
                if (string.IsNullOrWhiteSpace(queueName))
                    throw new InvalidOperationException("QueueName is required for Service Bus scoring ingestion.");

                var endpointUri = new Uri(endpoint);
                var httpsBuilder = new UriBuilder(endpointUri)
                {
                    Scheme = "https",
                    Port = -1
                };

                return new ServiceBusConnectionInfo(httpsBuilder.Uri, queueName, keyName, Convert.FromBase64String(sharedKey));
            }

            public string CreateSasToken(Uri relativeUri)
            {
                var targetUri = new Uri(BaseUri, relativeUri);
                var resource = targetUri.ToString().TrimEnd('/');
                var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
                var encodedResource = Uri.EscapeDataString(resource);
                var stringToSign = $"{encodedResource}\n{expiry}";
                using var hmac = new HMACSHA256(_sharedAccessKey);
                var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                var encodedSignature = Uri.EscapeDataString(signature);
                return $"SharedAccessSignature sr={encodedResource}&sig={encodedSignature}&se={expiry}&skn={_sharedAccessKeyName}";
            }
        }
    }
}
