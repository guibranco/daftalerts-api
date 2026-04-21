using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Parsing;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace DaftAlerts.Infrastructure.Ingestion;

/// <summary>
/// Reads a MIME stream, stores a <see cref="RawEmail"/>, runs the parser, and creates/updates the
/// corresponding <see cref="Property"/>. Idempotent on Message-Id (falls back to a hash of
/// Date+From+Subject if the header is missing).
/// </summary>
public sealed class EmailIngestionPipeline : IEmailIngestionPipeline
{
    private readonly IRawEmailRepository _rawEmails;
    private readonly IPropertyRepository _properties;
    private readonly IDaftEmailParser _parser;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<EmailIngestionPipeline> _logger;

    public EmailIngestionPipeline(
        IRawEmailRepository rawEmails,
        IPropertyRepository properties,
        IDaftEmailParser parser,
        IUnitOfWork uow,
        IClock clock,
        ILogger<EmailIngestionPipeline> logger
    )
    {
        _rawEmails = rawEmails;
        _properties = properties;
        _parser = parser;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(Stream mimeStream, CancellationToken ct)
    {
        // Buffer the stream so we can both parse and persist the raw bytes.
        byte[] rawBytes;
        await using (var buffer = new MemoryStream())
        {
            await mimeStream.CopyToAsync(buffer, ct);
            rawBytes = buffer.ToArray();
        }

        MimeMessage message;
        using (var ms = new MemoryStream(rawBytes, writable: false))
        {
            message = await MimeMessage.LoadAsync(ms, ct);
        }

        var messageId = ResolveMessageId(message);
        var subject = message.Subject ?? string.Empty;
        var receivedAt = (
            message.Date.UtcDateTime == DateTime.MinValue ? _clock.UtcNow : message.Date.UtcDateTime
        );

        if (await _rawEmails.ExistsByMessageIdAsync(messageId, ct))
        {
            _logger.LogInformation("Ingest: duplicate message-id={MessageId}, skipping", messageId);
            return new IngestionResult(
                IngestionOutcome.DuplicateIgnored,
                messageId,
                null,
                null,
                null
            );
        }

        var rawEmail = new RawEmail
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Subject = Truncate(subject, 512),
            ReceivedAt = receivedAt,
            RawMimeBytes = rawBytes,
            ParseStatus = ParseStatus.Pending,
            LastAttemptAt = _clock.UtcNow,
        };
        await _rawEmails.AddAsync(rawEmail, ct);

        var htmlBody = ExtractHtmlBody(message);
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            rawEmail.ParseStatus = ParseStatus.Failed;
            rawEmail.ParseError = "No HTML body found in message.";
            await _uow.SaveChangesAsync(ct);
            _logger.LogWarning("Ingest: no HTML body for message-id={MessageId}", messageId);
            return new IngestionResult(
                IngestionOutcome.ParseFailed,
                messageId,
                rawEmail.ParseError,
                null,
                rawEmail.Id
            );
        }

        var parsed = _parser.Parse(htmlBody, subject, receivedAt, messageId);
        if (parsed is null)
        {
            rawEmail.ParseStatus = ParseStatus.Failed;
            rawEmail.ParseError = "Parser returned null (not a recognized Daft alert layout).";
            await _uow.SaveChangesAsync(ct);
            _logger.LogWarning("Ingest: parse failed for message-id={MessageId}", messageId);
            return new IngestionResult(
                IngestionOutcome.NotADaftEmail,
                messageId,
                rawEmail.ParseError,
                null,
                rawEmail.Id
            );
        }

        // Upsert by DaftId
        var property = await _properties.GetByDaftIdAsync(parsed.DaftId, ct);
        var now = _clock.UtcNow;

        if (property is null)
        {
            property = new Property
            {
                Id = Guid.NewGuid(),
                DaftId = parsed.DaftId,
                DaftUrl = parsed.DaftUrl,
                Address = parsed.Address,
                Eircode = parsed.Eircode,
                RoutingKey = parsed.RoutingKey,
                PriceMonthly = parsed.PriceMonthly,
                Currency = "EUR",
                Beds = parsed.Beds,
                Baths = parsed.Baths,
                PropertyType = parsed.PropertyType,
                BerRating = parsed.BerRating,
                MainImageUrl = parsed.MainImageUrl,
                Status = PropertyStatus.Inbox,
                ReceivedAt = parsed.ReceivedAt,
                RawSubject = parsed.RawSubject,
                RawEmailMessageId = parsed.MessageId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _properties.AddAsync(property, ct);
        }
        else
        {
            // Re-alert on an existing listing: refresh mutable fields, keep status/notes.
            property.DaftUrl = parsed.DaftUrl;
            property.Address = parsed.Address;
            property.Eircode = parsed.Eircode ?? property.Eircode;
            property.RoutingKey = parsed.RoutingKey ?? property.RoutingKey;
            property.PriceMonthly = parsed.PriceMonthly;
            property.Beds = parsed.Beds;
            property.Baths = parsed.Baths;
            property.PropertyType = parsed.PropertyType;
            property.BerRating = parsed.BerRating ?? property.BerRating;
            property.MainImageUrl = parsed.MainImageUrl ?? property.MainImageUrl;
            property.ReceivedAt = parsed.ReceivedAt;
            property.RawSubject = parsed.RawSubject;
            property.UpdatedAt = now;
        }

        rawEmail.ParseStatus = ParseStatus.Parsed;
        rawEmail.PropertyId = property.Id;
        rawEmail.ParseError = null;

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Ingest: stored property daftId={DaftId} id={PropertyId}",
            parsed.DaftId,
            property.Id
        );
        return new IngestionResult(
            IngestionOutcome.Created,
            messageId,
            null,
            property.Id,
            rawEmail.Id
        );
    }

    // --- helpers ------------------------------------------------------------

    private static string ResolveMessageId(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
            return Hash("mid:" + message.MessageId);

        var parts = new[]
        {
            message.Date.ToString("O"),
            message.From.ToString(),
            message.Subject ?? string.Empty,
        };
        return Hash("fallback:" + string.Join("|", parts));
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string? ExtractHtmlBody(MimeMessage message)
    {
        // MimeKit exposes HtmlBody for the preferred HTML part (walks alternatives/attachments).
        var html = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(html))
            return html;

        // Fallback: if only a text body is present, wrap it so the parser has something to chew on.
        var text = message.TextBody;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><pre>" + HtmlEntity.Entitize(text) + "</pre></body></html>");
        return doc.DocumentNode.OuterHtml;
    }

    private static string Truncate(string input, int max) =>
        string.IsNullOrEmpty(input) || input.Length <= max ? input : input[..max];
}
