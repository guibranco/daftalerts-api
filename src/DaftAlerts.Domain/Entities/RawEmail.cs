using System;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Domain.Entities;

public sealed class RawEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }

    public byte[] RawMimeBytes { get; set; } = Array.Empty<byte>();

    public ParseStatus ParseStatus { get; set; } = ParseStatus.Pending;
    public string? ParseError { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    public Guid? PropertyId { get; set; }
}
