using System;

namespace DaftAlerts.Application.Parsing;

public interface IDaftEmailParser
{
    /// <summary>
    /// Parses a Daft.ie property alert email.
    /// </summary>
    /// <param name="htmlBody">HTML body of the email.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="receivedAt">Date the email was received (UTC).</param>
    /// <param name="messageId">MIME <c>Message-Id</c> header, if any.</param>
    /// <returns>The parsed result, or <c>null</c> if the email could not be parsed into a property.</returns>
    ParsedDaftEmail? Parse(
        string htmlBody,
        string? subject,
        DateTime receivedAt,
        string? messageId
    );
}
