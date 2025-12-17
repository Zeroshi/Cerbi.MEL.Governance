using Azure.Messaging.ServiceBus;
using Cerbi;
using Cerbi.Contracts;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Debugging;

namespace Cerbi
{
    /// <summary>
    /// Non-blocking, batched shipper for governance score events.
    /// Enqueue is fire-and-forget; background loop flushes batches.
    /// </summary>
    public class ScoreShipper : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ScoreShippingOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ScoringIngestionOptions _ingestionOptions;
        private readonly ServiceBusClient? _serviceBusClient;
        private readonly ServiceBusSender? _serviceBusSender;
        private readonly ConcurrentQueue<GovernanceScoreEvent> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;
        private int _sending = 0;

        public ScoreShipper(HttpClient httpClient, ScoreShippingOptions options)
            : this(httpHttpClient, options, new ScoringIngestionOptions(), NoopScoringQueueSender.Instance, null)
        {
        }

        public ScoreShipper(HttpClient httpHttpClient, ScoreShippingOptions options, ScoringIngestionOptions? ingestionOptions = null)
        {
            _httpClient = httpHttpClient;
            _options = options ?? new ScoreShippingOptions();
            _ingestionOptions = ingestionOptions ?? new ScoringIngestionOptions();
            (_serviceBusClient, _serviceBusSender) = CreateServiceBusSender(_ingestionOptions);
            _worker = Task.Run(WorkerLoop);
        }

        public virtual void Enqueue(ScoringQueueEnvelopeDto envelope)
        {
            if (!_options.Enabled || !_options.LicenseAllowsScoring)
                return;

            if (!ShouldUseQueue && !ShouldUseHttp)
            {
                Warn("Score shipping skipped: no queue or HTTP transport configured.");
                return;
            }

            if (_queue.Count >= _options.MaxQueueSize) return; // drop when full
            _queue.Enqueue(envelope);
        }

        [Obsolete("Use Enqueue(ScoringQueueEnvelopeDto) instead.")]
        public virtual void Enqueue(GovernanceScoreEvent ev)
        {
            var envelope = new ScoringQueueEnvelopeDto
            {
                IdempotencyKey = ev.IdempotencyKey,
                CorrelationId = ev.CorrelationId,
                TenantId = ev.TenantId,
                AppName = ev.AppName,
                Environment = ev.Environment,
                Payload = new ScoringEventDto
                {
                    IdempotencyKey = ev.IdempotencyKey,
                    CorrelationId = ev.CorrelationId,
                    TenantId = ev.TenantId,
                    AppName = ev.AppName,
                    Environment = ev.Environment,
                    Topic = ev.Topic,
                    Category = ev.Category,
                    LogId = ev.LogId,
                    EventId = ev.EventId,
                    EventName = ev.EventName,
                    ScoreImpact = ev.ScoreImpact,
                    GovernanceRelaxed = ev.GovernanceRelaxed,
                    Timestamp = ev.Timestamp,
                    Violations = ev.Violations,
                    Fields = ev.Fields ?? new Dictionary<string, object?>(StringComparer.Ordinal)
                }
            };
            Enqueue(envelope);
        }

        internal void FlushForTesting() => FlushOnce();

        private async Task WorkerLoopAsync()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.FlushIntervalSeconds), token).ConfigureAwait(false);
                    FlushOnce();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Warn($"Score shipping loop error: {ex.Message}");
                }
            }
        }

        private void FlushOnce()
        {
            if (Interlocked.Exchange(ref _sending, 1) == 1) return;
            try
            {
                if (_queue.IsEmpty) return;
                var batch = new List<ScoringQueueEnvelopeDto>(_options.BatchSize);
                while (batch.Count < _options.BatchSize && _queue.TryDequeue(out var ev))
                {
                    batch.Add(ev);
                }
                if (batch.Count == 0) return;
                _ = SendWithRetryAsync(batch);
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private async Task SendWithRetryAsync(List<ScoringQueueEnvelopeDto> batch)
        {
            var queueUsed = false;
            if (ShouldUseQueue)
            {
                try
                {
                    foreach (var envelope in batch)
                    {
                        await _serviceBusSender.SendMessageAsync(envelope.ToServiceBusMessage(), _cts.Token).ConfigureAwait(false);
                    }
                    queueUsed = true;
                    if (_ingestionOptions.Mode == ScoringIngestionMode.QueueOnly)
                        return;
                }
                catch (Exception ex)
                {
                    Warn($"Service Bus send failed: {ex.Message}");
                    if (_ingestionOptions.Mode == ScoringIngestionMode.QueueOnly)
                        return;
                }
            }

            if (!ShouldUseHttp)
            {
                if (!queueUsed)
                {
                    Warn("Score shipping dropped batch: no transport succeeded.");
                }
                return;
            }

            var payload = JsonSerializer.Serialize(batch, SerializerOptions);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var resp = await _httpClient.PostAsync(_options.Endpoint, content, _cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) return;
                }
                catch (Exception ex)
                {
                    Warn($"HTTP score send attempt failed: {ex.Message}");
                }
                await Task.Delay(_options.RetryDelayMilliseconds).ConfigureAwait(false);
            }
        }

        private async Task SendQueueAsync(IReadOnlyList<ScoringQueueEnvelopeDto> envelopes)
        {
            if (_serviceBusSender is null)
            {
                SelfLog.WriteLine("[CerbiGovernance] Service Bus sender is not configured.");
                return;
            }

            foreach (var envelope in envelopes)
            {
                try
                {
                    var message = BuildServiceBusMessage(envelope);
                    await _serviceBusSender.SendMessageAsync(message, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("[CerbiGovernance] Failed to send scoring event to Service Bus: {0}", ex);
                }
            }
        }

        private ServiceBusMessage BuildServiceBusMessage(ScoringQueueEnvelopeDto envelope)
        {
            var payload = JsonSerializer.Serialize(envelope, SerializerOptions);
            var body = Encoding.UTF8.GetBytes(payload);
            var message = new ServiceBusMessage(body)
            {
                ContentType = "application/json",
                MessageId = envelope.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
                CorrelationId = envelope.CorrelationId ?? envelope.Payload?.CorrelationId,
                Subject = envelope.Payload?.Topic
            };
            return message;
        }

        private bool ShouldUseQueue => _serviceBusSender != null && _ingestionOptions.Mode != ScoringIngestionMode.HttpOnly;
        private bool ShouldUseHttp => !string.IsNullOrWhiteSpace(_options.Endpoint) && _ingestionOptions.Mode != ScoringIngestionMode.QueueOnly;

        private void Warn(string message)
        {
            if (_warn != null)
            {
                _warn(message);
            }
            else
            {
                Trace.TraceWarning(message);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _worker.Wait(500); } catch { }
            _cts.Dispose();
            _serviceBusClient.Dispose();
        }

        private static (ServiceBusClient? Client, ServiceBusSender? Sender) CreateServiceBusSender(ScoringIngestionOptions? ingestionOptions)
        {
            var azure = ingestionOptions?.AzureServiceBus;
            if (azure == null) return (null, null);
            if (string.IsNullOrWhiteSpace(azure.ConnectionString) || string.IsNullOrWhiteSpace(azure.QueueName))
            {
                return (null, null);
            }

            try
            {
                var client = new ServiceBusClient(azure.ConnectionString);
                var sender = client.CreateSender(azure.QueueName);
                return (client, sender);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("[CerbiGovernance] Failed to create Service Bus sender: {0}", ex);
                return (null, null);
            }
        }
    }
}
