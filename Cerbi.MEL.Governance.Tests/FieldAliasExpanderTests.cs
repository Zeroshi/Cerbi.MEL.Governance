using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Cerbi.Tests
{
    public sealed class FieldAliasExpanderTests
    {
        [Fact]
        public void WrappedAliases_UseRequestedProfile()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": {
                "Orders": { "fieldAliases": { "requestId": ["rid"] } },
                "Payments": { "fieldAliases": { "requestId": ["paymentRid"] } }
              }
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");
            var fields = new Dictionary<string, object> { ["rid"] = "r-1", ["paymentRid"] = "p-1" };

            expander.ExpandAliases(fields);

            Assert.Equal("r-1", fields["requestId"]);
        }

        [Fact]
        public void WrappedAliases_ExactMatchWinsOverCaseInsensitiveFallback()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": {
                "orders": { "fieldAliases": { "requestId": ["fallbackRid"] } },
                "Orders": { "fieldAliases": { "requestId": ["exactRid"] } }
              }
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");
            var fields = new Dictionary<string, object> { ["exactRid"] = "exact", ["fallbackRid"] = "fallback" };

            expander.ExpandAliases(fields);

            Assert.Equal("exact", fields["requestId"]);
        }

        [Fact]
        public void WrappedAliases_OneCaseInsensitiveFallbackSucceeds()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "loggingprofiles": {
                "orders": { "fieldAliases": { "requestId": ["rid"] } }
              }
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");
            var fields = new Dictionary<string, object> { ["rid"] = "r-1" };

            expander.ExpandAliases(fields);

            Assert.Equal("r-1", fields["requestId"]);
        }

        [Fact]
        public void WrappedAliases_MultipleCaseInsensitiveMatchesThrow()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": {
                "orders": { "fieldAliases": { "requestId": ["rid1"] } },
                "ORDERS": { "fieldAliases": { "requestId": ["rid2"] } }
              }
            }
            """);

            Assert.Throws<InvalidDataException>(() => FieldAliasExpander.LoadFromConfig(file.Path, "Orders"));
        }

        [Fact]
        public void WrappedAliases_MissingRequestedProfileThrows()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": {
                "Payments": { "fieldAliases": { "requestId": ["paymentRid"] } }
              }
            }
            """);

            Assert.Throws<InvalidDataException>(() => FieldAliasExpander.LoadFromConfig(file.Path, "Orders"));
        }

        [Fact]
        public void WrappedAliases_NonObjectLoggingProfilesThrows()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": ["Orders"]
            }
            """);

            Assert.Throws<InvalidDataException>(() => FieldAliasExpander.LoadFromConfig(file.Path, "Orders"));
        }

        [Fact]
        public void CanonicalRootProfile_WorksWhenLoggingProfilesMissing()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "name": "Orders",
              "version": "2026.07",
              "requiredFields": ["requestId"],
              "disallowedFields": ["password"],
              "fieldSeverities": {}
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");

            Assert.False(expander.HasAliases);
        }

        [Fact]
        public void RootFieldAliases_WorkWhenLoggingProfilesMissing()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "name": "Orders",
              "version": "2026.07",
              "requiredFields": ["requestId"],
              "disallowedFields": ["password"],
              "fieldSeverities": {},
              "fieldAliases": { "requestId": ["rid"] }
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");
            var fields = new Dictionary<string, object> { ["rid"] = "r-1" };

            expander.ExpandAliases(fields);

            Assert.Equal("r-1", fields["requestId"]);
        }

        [Fact]
        public void WrappedAliases_ComeOnlyFromSelectedProfile()
        {
            using var file = TempGovernanceFile.Create("""
            {
              "LoggingProfiles": {
                "Orders": { "fieldAliases": { "requestId": ["rid"] } },
                "Payments": { "fieldAliases": { "paymentId": ["pid"] } }
              }
            }
            """);

            var expander = FieldAliasExpander.LoadFromConfig(file.Path, "Orders");
            var fields = new Dictionary<string, object> { ["pid"] = "p-1", ["rid"] = "r-1" };

            expander.ExpandAliases(fields);

            Assert.Equal("r-1", fields["requestId"]);
            Assert.False(fields.ContainsKey("paymentId"));
        }

        private sealed class TempGovernanceFile : IDisposable
        {
            private TempGovernanceFile(string path) => Path = path;

            public string Path { get; }

            public static TempGovernanceFile Create(string json)
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cerbi-governance-{Guid.NewGuid():N}.json");
                File.WriteAllText(path, json);
                return new TempGovernanceFile(path);
            }

            public void Dispose()
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
        }
    }
}
