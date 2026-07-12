using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Errors;
using CodeKnowledge.Core.Git;
using CodeKnowledge.Core.Hashing;
using CodeKnowledge.Core.Knowledge;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Core.Tests.Fakes;

namespace CodeKnowledge.Core.Tests;

public sealed class SaveKnowledgeUseCaseTests
{
    private readonly FakeGitRepository _git = new();
    private readonly FakeProjectStore _projectStore = new();
    private readonly FakeKnowledgeStore _knowledgeStore = new();

    private SaveKnowledgeUseCase UseCase => new(
        new ResolveProjectUseCase(_git, _projectStore), _git, _knowledgeStore);

    public SaveKnowledgeUseCaseTests()
    {
        _git.Context = new GitContext(
            @"C:\work\order-api", "abc123", "main",
            new Dictionary<string, string> { ["origin"] = "https://github.com/company/order-api.git" },
            null, null);
        _git.FilesAtCommit["src/OrderService.cs"] = "class OrderService\n{\n    void Complete() { }\n}\n";
    }

    private static SaveKnowledgeRequest Request(
        IReadOnlyList<SaveFactInput>? facts = null,
        string confidence = "high",
        IReadOnlyList<SaveRelationInput>? relations = null,
        string filePath = @"C:\work\order-api\src\OrderService.cs",
        IReadOnlyList<SaveEvidenceInput>? evidence = null)
        => new(
            WorkingDirectory: @"C:\work\order-api",
            CanonicalKey: "domain.mail.order-completed",
            Title: "注文完了メール仕様",
            OriginalQuestion: "注文完了メールの処理は？",
            Summary: "OrderServiceがメールを送る",
            Confidence: confidence,
            Tags: "mail order",
            CreatedBy: "test-agent",
            CommitHash: null,
            Evidence: evidence ?? [new SaveEvidenceInput(filePath, null, "OrderService", "class", null, 1, 4, null)],
            Facts: facts ?? [new SaveFactInput("OrderServiceがメール送信を行う", [0])],
            Inferences: [],
            Relations: relations ?? []);

    [Fact]
    public void Execute_saves_and_returns_result()
    {
        var result = UseCase.Execute(Request());

        Assert.Equal("github.com/company/order-api", result.ProjectId);
        Assert.True(result.CreatedNewKnowledge);
        var saved = Assert.Single(_knowledgeStore.SavedVersions);
        Assert.Equal("abc123", saved.CommitHash);
        Assert.Equal(Confidence.High, saved.Confidence);
    }

    [Fact]
    public void Execute_rejects_fact_without_evidence() // AC-09
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(facts: [new SaveFactInput("根拠なしの事実", [])])));
        Assert.Equal(CodeKnowledgeException.FactRequiresEvidence, exception.Code);
        Assert.Empty(_knowledgeStore.SavedVersions);
    }

    [Theory]
    [InlineData("0.9")]
    [InlineData("certain")]
    [InlineData("")]
    public void Execute_rejects_undefined_confidence(string confidence) // AC-28
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(confidence: confidence)));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
        Assert.Empty(_knowledgeStore.SavedVersions);
    }

    [Fact]
    public void Execute_rejects_unknown_relation_kind()
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(relations: [new SaveRelationInput("A", "B", "depends-on")])));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Execute_rejects_out_of_range_evidence_index()
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(facts: [new SaveFactInput("事実", [5])])));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
    }

    [Fact]
    public void Execute_normalizes_evidence_file_path() // 要件6.3
    {
        UseCase.Execute(Request(filePath: @"C:\work\order-api\src\OrderService.cs"));
        var evidence = Assert.Single(_knowledgeStore.SavedVersions[0].Evidence);
        Assert.Equal("src/OrderService.cs", evidence.FilePath);
    }

    [Fact]
    public void Execute_computes_hashes_from_commit_content()
    {
        UseCase.Execute(Request());
        var evidence = Assert.Single(_knowledgeStore.SavedVersions[0].Evidence);
        Assert.Equal(
            ContentHasher.ComputeFileHash(_git.FilesAtCommit["src/OrderService.cs"]),
            evidence.FileHash);
        Assert.Equal(
            ContentHasher.ComputeSymbolHash(_git.FilesAtCommit["src/OrderService.cs"], 1, 4),
            evidence.SymbolHash);
    }

    [Fact]
    public void Execute_returns_similar_knowledge_warning() // 要件6.1
    {
        _knowledgeStore.Summaries.Add(new KnowledgeSummary(
            "knowledge-9", "domain.mail.order-completed.v2", "注文完了メールの仕様まとめ",
            "...", "abc", Confidence.High, DateTimeOffset.UtcNow));

        var result = UseCase.Execute(Request());

        Assert.Single(result.SimilarKnowledge);
    }

    [Theory]
    [InlineData(0, 4)] // startLine < 1
    [InlineData(4, 1)] // endLine < startLine (inverted)
    public void Execute_rejects_invalid_evidence_line_range(int startLine, int endLine) // data-integrity: avoid silently hashing empty content
    {
        var exception = Assert.Throws<CodeKnowledgeException>(() =>
            UseCase.Execute(Request(evidence:
                [new SaveEvidenceInput(
                    @"C:\work\order-api\src\OrderService.cs", null, "OrderService", "class", null,
                    startLine, endLine, null)])));
        Assert.Equal(CodeKnowledgeException.InvalidArguments, exception.Code);
        Assert.Empty(_knowledgeStore.SavedVersions);
    }
}
