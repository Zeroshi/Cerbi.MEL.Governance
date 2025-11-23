using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Cerbi
{
    public class CerbiGovernanceLogger : ILogger, ISupportExternalScope
    {
        private static readonly ConcurrentDictionary<string, string?> CategoryTopicCache = new();
        private static readonly ConcurrentDictionary<Type, string?> TypeTopicCache = new();
        private static readonly AsyncLocal<string?> ScopeTopic = new();
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = false };

        private readonly ILogger _inner;
        private readonly RuntimeGovernanceValidator _validator;
        private readonly string _defaultTopic;
        private readonly string _categoryName;
        private readonly Func<bool>? _isGovernanceEnabled;
        private string? _cachedStackTraceTopic; // empty string means "none"
        private bool _stackTraceChecked;
        private readonly ScoreShipper _scoreShipper;
        private readonly CerbiGovernanceMELSettings _settings;
        private IExternalScopeProvider? _scopeProvider;

        // Compact constructor chain
        public CerbiGovernanceLogger(ILogger inner, RuntimeGovernanceValidator validator, string defaultTopic)
            : this(inner, validator, defaultTopic, null, null, null, null) { }
        public CerbiGovernanceLogger(ILogger inner, RuntimeGovernanceValidator validator, string defaultTopic, string? categoryName)
            : this(inner, validator, defaultTopic, categoryName, null, null, null) { }
        public CerbiGovernanceLogger(ILogger inner, RuntimeGovernanceValidator validator, string defaultTopic, string? categoryName, Func<bool>? isGovernanceEnabled)
            : this(inner, validator, defaultTopic, categoryName, isGovernanceEnabled, null, null) { }
        public CerbiGovernanceLogger(ILogger inner, RuntimeGovernanceValidator validator, string defaultTopic, string? categoryName, Func<bool>? isGovernanceEnabled, ScoreShipper? shipper, CerbiGovernanceMELSettings? settings)
        {
            _inner = inner;
            _validator = validator;
            _defaultTopic = defaultTopic ?? string.Empty;
            _categoryName = categoryName ?? string.Empty;
            _isGovernanceEnabled = isGovernanceEnabled;
            _scoreShipper = shipper ?? new ScoreShipper(new System.Net.Http.HttpClient(), new ScoreShippingOptions());
            _settings = settings ?? new CerbiGovernanceMELSettings();
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            // If caller uses CerbiTopicScope, set AsyncLocal for fast topic override
            string? prev = null;
            if (state is CerbiTopicScope scope)
            {
                prev = ScopeTopic.Value;
                ScopeTopic.Value = scope.Topic;
            }
            var innerScope = _inner.BeginScope(state);
            if (_scopeProvider != null)
            {
                innerScope = new CompositeScope(innerScope, _scopeProvider.Push(state));
            }
            if (prev == null && !(state is CerbiTopicScope)) return innerScope;
            return new TopicScopeReset(innerScope, prev);
        }

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // 0) If governance is disabled or mode is Off, delegate directly
            if ((_isGovernanceEnabled != null && !_isGovernanceEnabled()) || _settings.EnforcementMode == GovernanceEnforcementMode.Off)
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            // 0a) Respect MinValidationLevel and SamplingRate
            if (logLevel < _settings.MinValidationLevel)
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }
            if (_settings.SamplingRate < 1.0 && Random.Shared.NextDouble() > _settings.SamplingRate)
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            //1) Determine Cerbi topic (scope → attribute/category → default)
            var topic = ScopeTopic.Value;
            if (string.IsNullOrWhiteSpace(topic))
            {
                var topicFromAttribute = ResolveTopic();
                topic = !string.IsNullOrWhiteSpace(topicFromAttribute) ? topicFromAttribute : _defaultTopic;
            }

            //2) If no topic at all, delegate directly—no Cerbi enrichment
            if (string.IsNullOrWhiteSpace(topic))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            //3) Extract structured fields from “state” if possible (low-allocation)
            var fields = ExtractFields(state);

            //4) Inject the “CerbiTopic” so the validator knows which profile to use
            fields["CerbiTopic"] = topic;

            //5) Run governance-validation
            var validated = _validator.Validate(fields);

            //6) If there are violations, record them; otherwise record status depending on mode
            bool hasViolation = false;
            IEnumerable<string>? violationsEnum = null;
            if (validated.TryGetValue("GovernanceViolations", out var v) && v is IEnumerable<string> cand)
            {
                using var e = cand.GetEnumerator();
                if (e.MoveNext())
                {
                    hasViolation = true;
                    violationsEnum = EnumerateWithFirst(cand, e.Current);
                }
            }

            if (hasViolation)
            {
                fields["GovernanceViolations"] = violationsEnum!.ToArray();
                fields["GovernanceRelaxed"] = false;
                fields["GovernanceProfileUsed"] = topic;
                fields["GovernanceMode"] = _settings.EnforcementMode.ToString();
            }
            else
            {
                fields["GovernanceProfileUsed"] = topic;
                if (_settings.EnforcementMode == GovernanceEnforcementMode.Strict)
                {
                    fields["GovernanceEnforced"] = true;
                }
                fields["GovernanceMode"] = _settings.EnforcementMode.ToString();
            }

            //7a) Always log the original message exactly as the caller wrote it
            _inner.Log(logLevel, eventId, state, exception, formatter);

            //7b) Only if there was at least one violation, serialize “fields” to JSON and log it
            if (hasViolation)
            {
                string jsonPayload = JsonSerializer.Serialize(fields, JsonOpts);
                _inner.Log(
                    logLevel,
                    eventId,
                    jsonPayload,
                    exception,
                    (msg, ex) => msg!
                );
            }

            // Score shipping extraction (non-blocking)
            TryShipScore(fields);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        private void TryShipScore(Dictionary<string, object> fields)
        {
            if (!_settings.ScoreShipping.Enabled || !_settings.ScoreShipping.LicenseAllowsScoring) return;
            if (!fields.TryGetValue("GovernanceScoreImpact", out var rawImpact)) return;
            if (!double.TryParse(rawImpact?.ToString(), out var impact)) return;
            var relaxed = fields.TryGetValue("GovernanceRelaxed", out var r) && r is bool b && b;
            GovernanceViolationSummary[] summaries = Array.Empty<GovernanceViolationSummary>();
            if (fields.TryGetValue("GovernanceViolations", out var rawViolations))
            {
                if (rawViolations is IEnumerable<string> vs)
                {
                    summaries = vs.Select(x => new GovernanceViolationSummary { Code = x, Rule = x }).ToArray();
                }
                else if (rawViolations is IEnumerable<object> objs)
                {
                    summaries = objs.Select(o => new GovernanceViolationSummary { Code = o?.ToString() ?? string.Empty, Rule = o?.ToString() ?? string.Empty }).ToArray();
                }
            }
            var ev = new GovernanceScoreEvent
            {
                AppName = _settings.AppName,
                Environment = _settings.Environment,
                Timestamp = DateTimeOffset.UtcNow,
                ScoreImpact = impact,
                GovernanceRelaxed = relaxed,
                Violations = summaries
            };
            _scoreShipper.Enqueue(ev);
        }

        private static IEnumerable<string> EnumerateWithFirst(IEnumerable<string> source, string first)
        {
            yield return first;
            foreach (var s in source)
            {
                if (ReferenceEquals(s, first) || (s != null && s.Equals(first)))
                {
                    first = null!;
                    continue;
                }
                yield return s;
            }
        }

        private string? ResolveTopic()
        {
            //1) Try fast path via logger category cache
            if (!string.IsNullOrEmpty(_categoryName))
            {
                if (CategoryTopicCache.TryGetValue(_categoryName, out var cached))
                    return string.IsNullOrEmpty(cached) ? null : cached;

                var topic = ResolveTopicFromCategoryName(_categoryName);
                CategoryTopicCache[_categoryName] = topic ?? string.Empty;
                return topic;
            }

            //2) Fallback: do a single StackTrace scan per instance and cache the result
            if (!_stackTraceChecked)
            {
                _stackTraceChecked = true;
                _cachedStackTraceTopic = ResolveTopicFromStackTrace() ?? string.Empty;
            }
            return string.IsNullOrEmpty(_cachedStackTraceTopic) ? null : _cachedStackTraceTopic;
        }

        private static string? ResolveTopicFromCategoryName(string categoryName)
        {
            var type = Type.GetType(categoryName, throwOnError: false, ignoreCase: false)
                       ?? AppDomain.CurrentDomain.GetAssemblies()
                          .Select(a => a.GetType(categoryName, throwOnError: false, ignoreCase: false))
                          .FirstOrDefault(t => t != null);
            if (type == null)
                return null;

            if (TypeTopicCache.TryGetValue(type, out var cached))
                return string.IsNullOrEmpty(cached) ? null : cached;

            var attr = type.GetCustomAttribute<CerbiTopicAttribute>(inherit: true);
            var topic = attr?.TopicName;
            TypeTopicCache[type] = topic ?? string.Empty;
            return topic;
        }

        private static string? ResolveTopicFromStackTrace()
        {
            var stack = new StackTrace();
            foreach (var frame in stack.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var declaring = frame.GetMethod()?.DeclaringType;
                if (declaring == null) continue;

                var fullName = declaring.FullName;
                if (string.IsNullOrWhiteSpace(fullName)
                    || fullName.StartsWith("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TypeTopicCache.TryGetValue(declaring, out var cached))
                    return string.IsNullOrEmpty(cached) ? null : cached;

                var attr = declaring.GetCustomAttributes(typeof(CerbiTopicAttribute), inherit: true)
                               .FirstOrDefault() as CerbiTopicAttribute;
                var topic = attr?.TopicName;
                TypeTopicCache[declaring] = topic ?? string.Empty;
                if (!string.IsNullOrEmpty(topic))
                    return topic;
            }

            return null;
        }

        private static Dictionary<string, object> ExtractFields<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                if (kvps is ICollection<KeyValuePair<string, object>> coll)
                {
                    var dict = new Dictionary<string, object>(coll.Count + 4, StringComparer.Ordinal);
                    foreach (var kv in coll)
                        dict[kv.Key] = kv.Value!;
                    return dict;
                }
                else
                {
                    var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var kv in kvps)
                        dict[kv.Key] = kv.Value!;
                    return dict;
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "Message", state?.ToString() ?? string.Empty }
            };
        }

        public readonly struct CerbiTopicScope
        {
            public CerbiTopicScope(string topic) => Topic = topic;
            public string Topic { get; }
        }

        private sealed class TopicScopeReset : IDisposable
        {
            private readonly IDisposable? _inner;
            private readonly string? _prev;
            public TopicScopeReset(IDisposable? inner, string? prev)
            {
                _inner = inner;
                _prev = prev;
            }
            public void Dispose()
            {
                ScopeTopic.Value = _prev;
                _inner?.Dispose();
            }
        }

        private sealed class CompositeScope : IDisposable
        {
            private readonly IDisposable? _a;
            private readonly IDisposable? _b;
            public CompositeScope(IDisposable? a, IDisposable? b)
            {
                _a = a;
                _b = b;
            }
            public void Dispose()
            {
                try { _a?.Dispose(); } finally { _b?.Dispose(); }
            }
        }
    }
}
