using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly HttpClient _httpClient;
        private readonly ScoreShippingOptions _options;
        private readonly ConcurrentQueue<GovernanceScoreEvent> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _drainActive;

        public ScoreShipper(HttpClient httpClient, ScoreShippingOptions options)
        {
            _httpClient = httpClient;
            _options = options;
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
            }
            _loop = Task.Run(RunAsync);
        }

        public virtual void Enqueue(GovernanceScoreEvent ev)
        {
            if (!_options.Enabled || !_options.LicenseAllowsScoring) return;
            if (_queue.Count >= _options.MaxQueueSize) return; // drop when full
            _queue.Enqueue(ev);
        }

        private async Task RunAsync()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.FlushIntervalSeconds), token);
                    FlushOnce();
                }
                catch (OperationCanceledException) { }
                catch { /* swallow */ }
            }
        }

        private void FlushOnce()
        {
            if (Interlocked.Exchange(ref _drainActive, 1) == 1) return;
            try
            {
                if (_queue.IsEmpty) return;
                var batch = new List<GovernanceScoreEvent>(_options.BatchSize);
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

        private async Task SendWithRetryAsync(List<GovernanceScoreEvent> batch)
        {
            if (string.IsNullOrWhiteSpace(_options.Endpoint)) return; // nothing to send
            var payload = JsonSerializer.Serialize(batch);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var resp = await _httpClient.PostAsync(_options.Endpoint, content, _cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) return;
                }
                catch { }
                await Task.Delay(_options.RetryDelayMilliseconds);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _loop.Wait(500); } catch { }
            _cts.Dispose();
        }
    }
}
