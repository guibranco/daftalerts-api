using System;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;

namespace DaftAlerts.Application.Services;

public static class PropertyStatusTransitions
{
    /// <summary>
    /// Applies a status transition with the correct timestamp bookkeeping. Returns true if the status actually changed.
    /// </summary>
    public static bool Transition(Property property, PropertyStatus newStatus, IClock clock)
    {
        if (property.Status == newStatus)
            return false;

        property.Status = newStatus;
        var now = clock.UtcNow;
        property.UpdatedAt = now;

        switch (newStatus)
        {
            case PropertyStatus.Approved:
                property.ApprovedAt = now;
                break;
            case PropertyStatus.Recycled:
                property.RecycledAt = now;
                break;
            case PropertyStatus.Inbox:
                // restore — clear both terminal timestamps
                property.ApprovedAt = null;
                property.RecycledAt = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(newStatus), newStatus, null);
        }

        return true;
    }

    public static PropertyStatus FromBulkAction(string action) =>
        action.ToLowerInvariant() switch
        {
            "approve" => PropertyStatus.Approved,
            "recycle" => PropertyStatus.Recycled,
            "restore" => PropertyStatus.Inbox,
            _ => throw new ArgumentException($"Unknown bulk action '{action}'.", nameof(action)),
        };
}
