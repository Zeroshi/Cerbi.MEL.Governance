# Cerbi.Contracts

Canonical scoring contracts shared between logger adapters, the Scoring API, and any analytics services that need to understand scoring payloads.

## Installation
Add the package reference (after publishing to your feed) to any producer or consumer that needs to understand scoring payloads:

```xml
<ItemGroup>
  <PackageReference Include="Cerbi.Contracts" Version="1.*" />
</ItemGroup>
```

## Usage
Reference the DTOs when producing payloads and keep `ContractVersions` in sync so consumers can branch safely:

```csharp
using Cerbi.Contracts.Contracts;

var payload = new ScoringEventDto
{
    SchemaVersion = ContractVersions.ScoringEventSchemaVersion,
    AppName = "ScoringWorker",
    TimestampUtc = DateTime.UtcNow,
    Score = new ScoreBreakdownDto { Overall = 88 }
};

var envelope = new ScoringQueueEnvelopeDto
{
    EnvelopeVersion = ContractVersions.ScoringEnvelopeVersion,
    EnqueuedUtc = DateTime.UtcNow,
    Payload = payload
};
```

Emit the DTOs as JSON using the exact property names defined in the contracts so downstream analytics services can deserialize reliably.

## JSON serialization rules
- Payloads use the property names defined in the DTOs; do not rely on runtime naming policies to rename fields.
- If a service enforces `camelCase` serialization, apply explicit `JsonPropertyName` attributes locally so the JSON produced matches these contract names.
- Nullable properties must be emitted as `null` rather than dropped whenever the value is unknown.
- All timestamps represent UTC instants using ISO-8601 strings.
- The queue envelope always sets `MessageType` to `"scoring-event"` and includes `EnvelopeVersion` for forward compatibility.

## Contract reference
| DTO | Key properties |
| --- | --- |
| `ScoringEventDto` | `SchemaVersion`, `AppName`, `TimestampUtc`, optional `Score`, `Violations`, and `GovernanceFlags` |
| `ScoringQueueEnvelopeDto` | `EnvelopeVersion`, constant `MessageType`, producer metadata, and the scoring payload |
| `ScoreBreakdownDto` | Optional numeric buckets for `Overall`, `Governance`, and `Safety` |
| `ViolationDto` | Optional `RuleId`, `Code`, `Field`, `Severity`, and `Message` annotations |
| `GovernanceFlagsDto` | `GovernanceRelaxed` indicates whether mitigations were applied |
| `ContractVersions` | Canonical version integers for schema/envelope coordination |

## Versioning
- `ContractVersions.ScoringEventSchemaVersion` and `ContractVersions.ScoringEnvelopeVersion` are the canonical integers to bump when the payload schema or envelope changes.
- Producers should populate `ScoringEventDto.SchemaVersion` and `ScoringQueueEnvelopeDto.EnvelopeVersion` with these constants so consumers can branch safely.

## Testing
Run `dotnet test tests/Cerbi.Contracts.Tests/Cerbi.Contracts.Tests.csproj` to validate the DTO defaults and round-trip behavior before publishing a package.
