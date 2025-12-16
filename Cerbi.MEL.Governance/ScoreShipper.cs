using Cerbi.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cerbi
{
    /// <summary>
    /// Non-blocking, batched shipper for governance score events.
    /// Enqueue is fire-and-forget; background loop flushes batches.
    /// </summary>
    public class ScoreShipper : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ScoreShippingOptions _options;
        private readonly ScoringIngestionOptions _ingestionOptions;
        private readonly IScoringQueueSender _queueSender;
        private readonly Action<string>? _warn;
        private readonly ConcurrentQueue<ScoringQueueEnvelopeDto> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _drainActive;

        public ScoreShipper(HttpClient httpClient, ScoreShippingOptions options)
            : this(httpClient, options, new ScoringIngestionOptions(), NoopScoringQueueSender.Instance, null)
        {
        }

        internal ScoreShipper(HttpClient httpClient, ScoreShippingOptions options, ScoringIngestionOptions? ingestionOptions, IScoringQueueSender? queueSender, Action<string>? warn = null)
        {
            _httpClient = httpClient;
            _options = options;
            _ingestionOptions = ingestionOptions ?? new ScoringIngestionOptions();
            _queueSender = queueSender ?? NoopScoringQueueSender.Instance;
            _warn = warn;
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
            }
            _loop = Task.Run(RunAsync);
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

        private async Task RunAsync()
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
            if (Interlocked.Exchange(ref _drainActive, 1) == 1) return;
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
                Interlocked.Exchange(ref _drainActive, 0);
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
                        await _queueSender.SendAsync(envelope, _cts.Token).ConfigureAwait(false);
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

        private bool ShouldUseQueue => _queueSender.IsConfigured && _ingestionOptions.Mode != ScoringIngestionMode.HttpOnly;
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
            try { _loop.Wait(500); } catch { }
            _cts.Dispose();
            _queueSender.Dispose();
        }
    }
}
