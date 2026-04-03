using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Cerbi;
using CerbiShield.Contracts.Scoring;

namespace Cerbi
{
    public interface IScoreShipper : IDisposable
    {
        void Enqueue(ScoringEventDto ev);
    }

    internal sealed class ScoreShipper : IScoreShipper
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ScoreShippingOptions _options;
        private readonly ScoringIngestionOptions _ingestionOptions;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentQueue<ScoringEventDto> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;
        private readonly ServiceBusClient? _serviceBusClient;
        private readonly ServiceBusSender? _serviceBusSender;
        private int _sending;

        public ScoreShipper(HttpClient httpClient, ScoreShippingOptions options, ScoringIngestionOptions? ingestionOptions = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? new ScoreShippingOptions();
            _ingestionOptions = ingestionOptions ?? new ScoringIngestionOptions();
            (_serviceBusClient, _serviceBusSender) = CreateServiceBusSender(_ingestionOptions);
            _worker = Task.Run(WorkerLoop);
        }

        public void Enqueue(ScoringEventDto ev)
        {
            if (!_options.Enabled || !_options.LicenseAllowsScoring) return;
            if (_queue.Count >= _options.MaxQueueSize) return;
            _queue.Enqueue(ev);
        }

        // Overload accepting envelope directly (used by tests)
        public void Enqueue(ScoringQueueEnvelopeDto envelope)
        {
            if (!_options.Enabled || !_options.LicenseAllowsScoring) return;
            if (envelope?.Payload != null)
            {
                _queue.Enqueue(envelope.Payload);
            }
        }

        internal void FlushForTesting()
        {
            FlushBatchAsync().GetAwaiter().GetResult();
        }

        private async Task WorkerLoop()
        {
            var flushInterval = TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds));
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(flushInterval, _cts.Token).ConfigureAwait(false);
                    await FlushBatchAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogInternal("[CerbiGovernance] ScoreShipper worker failed: {0}", ex);
                }
            }

            try
            {
                await FlushBatchAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogInternal("[CerbiGovernance] ScoreShipper final flush failed: {0}", ex);
            }
        }

        private async Task FlushBatchAsync()
        {
            if (Interlocked.Exchange(ref _sending, 1) == 1) return;
            try
            {
                if (_queue.IsEmpty) return;

                var batch = ArrayPool<ScoringEventDto>.Shared.Rent(_options.BatchSize);
                var count = 0;

                try
                {
                    while (count < _options.BatchSize && _queue.TryDequeue(out var ev))
                    {
                        batch[count++] = ev;
                    }

                    if (count == 0) return;

                    var envelopes = new List<ScoringQueueEnvelopeDto>(count);
                    for (var i = 0; i < count; i++)
                    {
                        envelopes.Add(ScoringEnvelopeFactory.Create(batch[i]));
                    }

                    await DeliverAsync(envelopes).ConfigureAwait(false);
                }
                finally
                {
                    Array.Clear(batch, 0, count);
                    ArrayPool<ScoringEventDto>.Shared.Return(batch);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private async Task DeliverAsync(IReadOnlyList<ScoringQueueEnvelopeDto> envelopes)
        {
            var queueReady = _serviceBusSender != null;
            var httpReady = !string.IsNullOrWhiteSpace(_options.Endpoint);

            switch (_ingestionOptions.Mode)
            {
                case ScoringIngestionMode.QueueOnly:
                    if (!queueReady || !await SendQueueAsync(envelopes).ConfigureAwait(false))
                    {
                        LogInternal("[CerbiGovernance] QueueOnly ingestion configured but Service Bus is unavailable.");
                    }
                    break;
                case ScoringIngestionMode.HttpOnly:
                    if (httpReady)
                    {
                        await SendHttpBatchAsync(envelopes).ConfigureAwait(false);
                    }
                    else
                    {
                        LogInternal("[CerbiGovernance] HttpOnly ingestion configured but endpoint is missing.");
                    }
                    break;
                default:
                    var sent = queueReady && await SendQueueAsync(envelopes).ConfigureAwait(false);
                    if (!sent)
                    {
                        if (httpReady)
                        {
                            await SendHttpBatchAsync(envelopes).ConfigureAwait(false);
                        }
                        else
                        {
                            LogInternal("[CerbiGovernance] Score shipping skipped: no delivery target configured (QueueFirst mode).");
                        }
                    }
                    break;
            }
        }

        private async Task<bool> SendQueueAsync(IReadOnlyList<ScoringQueueEnvelopeDto> envelopes)
        {
            if (_serviceBusSender is null) return false;

            var success = true;
            foreach (var envelope in envelopes)
            {
                try
                {
                    var message = BuildServiceBusMessage(envelope);
                    await _serviceBusSender.SendMessageAsync(message, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    success = false;
                    LogInternal("[CerbiGovernance] Failed to send scoring event to Service Bus: {0}", ex);
                }
            }

            return success;
        }

        private ServiceBusMessage BuildServiceBusMessage(ScoringQueueEnvelopeDto envelope)
        {
            var payload = JsonSerializer.Serialize(envelope, SerializerOptions);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(payload))
            {
                ContentType = "application/json",
                MessageId = envelope.MessageId ?? Guid.NewGuid().ToString("N"),
                CorrelationId = envelope.Payload?.CorrelationId,
                Subject = envelope.Payload?.GovernanceProfile
            };
            return message;
        }

        private async Task SendHttpBatchAsync(IReadOnlyList<ScoringQueueEnvelopeDto> envelopes)
        {
            if (string.IsNullOrWhiteSpace(_options.Endpoint)) return;

            var payload = JsonSerializer.Serialize(envelopes, SerializerOptions);
            var attempt = 0;
            var maxAttempts = Math.Max(0, _options.MaxRetries);
            while (attempt <= maxAttempts)
            {
                try
                {
                    using var content = BuildHttpContent(payload);
                    var response = await _httpClient.PostAsync(_options.Endpoint, content, _cts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (Exception ex)
                {
                    LogInternal("[CerbiGovernance] HTTP score shipping failed: {0}", ex);
                }

                attempt++;
                if (attempt <= maxAttempts)
                {
                    await Task.Delay(ComputeBackoff(attempt), _cts.Token).ConfigureAwait(false);
                }
            }
        }

        private StringContent BuildHttpContent(string payload)
        {
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                content.Headers.Remove("X-Api-Key");
                content.Headers.Add("X-Api-Key", _options.ApiKey);
            }
            return content;
        }

        private TimeSpan ComputeBackoff(int attempt)
        {
            var baseDelayMs = Math.Max(100, _options.RetryDelayMilliseconds);
            var delay = baseDelayMs * Math.Pow(2, Math.Max(0, attempt - 1));
            var capped = Math.Min(delay, baseDelayMs * 8);
            return TimeSpan.FromMilliseconds(capped);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _worker.Wait(2000); } catch { }
            _cts.Dispose();

            try { _serviceBusSender?.DisposeAsync().AsTask().Wait(500); } catch { }
            try { _serviceBusClient?.DisposeAsync().AsTask().Wait(500); } catch { }
        }

        private static void LogInternal(string message, params object?[] args)
        {
            try
            {
                var formatted = (args != null && args.Length > 0)
                    ? string.Format(CultureInfo.InvariantCulture, message, args)
                    : message;
                Trace.WriteLine(formatted);
            }
            catch
            {
                // swallow logging failures to keep shipper resilient
            }
        }

        private static (ServiceBusClient? Client, ServiceBusSender? Sender) CreateServiceBusSender(ScoringIngestionOptions options)
        {
            var azure = options?.AzureServiceBus;
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
                LogInternal("[CerbiGovernance] Failed to create Service Bus sender: {0}", ex);
                return (null, null);
            }
        }
    }
}