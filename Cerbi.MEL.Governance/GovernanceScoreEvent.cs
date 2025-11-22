namespace Cerbi
{
    public class GovernanceScoreEvent
    {
        public string AppName { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public System.DateTimeOffset Timestamp { get; set; }
        public double ScoreImpact { get; set; }
        public bool GovernanceRelaxed { get; set; }
        public GovernanceViolationSummary[] Violations { get; set; } = System.Array.Empty<GovernanceViolationSummary>();
    }

    public class GovernanceViolationSummary
    {
        public string Code { get; set; } = string.Empty; // e.g., MissingField:userId
        public string Field { get; set; } = string.Empty; // extracted field name if applicable
        public string Rule { get; set; } = string.Empty; // raw rule string
    }
}
