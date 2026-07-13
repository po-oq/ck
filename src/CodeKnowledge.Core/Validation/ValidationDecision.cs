namespace CodeKnowledge.Core.Validation;

public sealed record ValidationDecisionResult(
    ValidationStatus Status, RecommendedAction RecommendedAction);

public static class ValidationDecision
{
    public static ValidationDecisionResult Decide(
        IReadOnlyList<EvidenceValidationStatus> evidence, bool? dirty)
    {
        if (dirty is null || evidence.Count == 0 ||
            evidence.Contains(EvidenceValidationStatus.Unknown))
            return new(ValidationStatus.Unknown, RecommendedAction.InspectEvidence);
        if (evidence.All(value => value == EvidenceValidationStatus.Unchanged))
            return new(ValidationStatus.Valid,
                dirty.Value ? RecommendedAction.InspectDirtyEvidence : RecommendedAction.ReuseKnowledge);
        if (evidence.All(value =>
                value is EvidenceValidationStatus.Changed or EvidenceValidationStatus.Missing))
            return new(ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge);
        return new(ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols);
    }
}
