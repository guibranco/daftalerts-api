using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaftAlerts.Application.Abstractions;

public enum IngestionOutcome
{
    Created,
    DuplicateIgnored,
    ParseFailed,
    NotADaftEmail
}

public sealed record IngestionResult(IngestionOutcome Outcome, string? MessageId, string? Error, System.Guid? PropertyId, System.Guid? RawEmailId);

public interface IEmailIngestionPipeline
{
    /// <summary>
    /// Reads a MIME message from <paramref name="mimeStream"/>, stores it as a <c>RawEmail</c>, parses it, and
    /// creates or updates a <c>Property</c>. Idempotent on Message-Id.
    /// </summary>
    Task<IngestionResult> IngestAsync(Stream mimeStream, CancellationToken ct);
}
