using CodeKnowledge.Core.Validation;

namespace CodeKnowledge.Core.Tests;

public sealed class ValidationDecisionTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_applies_the_approved_priority(
        EvidenceValidationStatus[] evidence, bool? dirty,
        ValidationStatus expectedStatus, RecommendedAction expectedAction)
    {
        var result = ValidationDecision.Decide(evidence, dirty);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedAction, result.RecommendedAction);
    }

    public static TheoryData<EvidenceValidationStatus[], bool?, ValidationStatus, RecommendedAction>
        Cases => new()
        {
            { [], false, ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
            { [EvidenceValidationStatus.Unchanged], false,
                ValidationStatus.Valid, RecommendedAction.ReuseKnowledge },
            { [EvidenceValidationStatus.Unchanged], true,
                ValidationStatus.Valid, RecommendedAction.InspectDirtyEvidence },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Changed], false,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Changed], true,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Missing], false,
                ValidationStatus.PartiallyStale, RecommendedAction.ReinspectChangedSymbols },
            { [EvidenceValidationStatus.Changed, EvidenceValidationStatus.Missing], false,
                ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge },
            { [EvidenceValidationStatus.Changed, EvidenceValidationStatus.Missing], true,
                ValidationStatus.Stale, RecommendedAction.ReinvestigateKnowledge },
            { [EvidenceValidationStatus.Unchanged, EvidenceValidationStatus.Unknown], false,
                ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
            { [EvidenceValidationStatus.Unchanged], null,
                ValidationStatus.Unknown, RecommendedAction.InspectEvidence },
        };
}
