using LinkMend.Core.Abstractions;

namespace LinkMend.Core.Models;

public enum CandidateConfidence
{
    Low,
    Medium,
    High
}

public sealed record ResolutionCandidate(
    string ExecutablePath,
    int Score,
    CandidateConfidence Confidence,
    IReadOnlyList<string> Reasons,
    FileMetadata? Metadata);

public sealed record RelocationSearchResult(
    IReadOnlyList<ResolutionCandidate> Candidates,
    IReadOnlyList<string> SearchedRoots,
    string? Notice);
