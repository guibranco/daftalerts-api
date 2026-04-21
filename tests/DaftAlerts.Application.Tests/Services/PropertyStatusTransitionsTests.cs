using System;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Services;
using DaftAlerts.Domain.Entities;
using DaftAlerts.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DaftAlerts.Application.Tests.Services;

public sealed class PropertyStatusTransitionsTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime t) { UtcNow = t; }
    }

    private static readonly DateTime Now = new(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Approve_sets_ApprovedAt_and_status()
    {
        var p = new Property { Status = PropertyStatus.Inbox };
        PropertyStatusTransitions.Transition(p, PropertyStatus.Approved, new FixedClock(Now)).Should().BeTrue();
        p.Status.Should().Be(PropertyStatus.Approved);
        p.ApprovedAt.Should().Be(Now);
        p.RecycledAt.Should().BeNull();
        p.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Recycle_sets_RecycledAt_and_status()
    {
        var p = new Property { Status = PropertyStatus.Inbox };
        PropertyStatusTransitions.Transition(p, PropertyStatus.Recycled, new FixedClock(Now)).Should().BeTrue();
        p.Status.Should().Be(PropertyStatus.Recycled);
        p.RecycledAt.Should().Be(Now);
        p.ApprovedAt.Should().BeNull();
    }

    [Fact]
    public void Restore_clears_both_timestamps()
    {
        var p = new Property
        {
            Status = PropertyStatus.Approved,
            ApprovedAt = Now.AddDays(-1),
            RecycledAt = Now.AddDays(-2)
        };
        PropertyStatusTransitions.Transition(p, PropertyStatus.Inbox, new FixedClock(Now)).Should().BeTrue();
        p.Status.Should().Be(PropertyStatus.Inbox);
        p.ApprovedAt.Should().BeNull();
        p.RecycledAt.Should().BeNull();
    }

    [Fact]
    public void No_op_when_status_unchanged()
    {
        var p = new Property { Status = PropertyStatus.Approved, UpdatedAt = Now.AddDays(-5) };
        PropertyStatusTransitions.Transition(p, PropertyStatus.Approved, new FixedClock(Now)).Should().BeFalse();
        p.UpdatedAt.Should().Be(Now.AddDays(-5));
    }

    [Theory]
    [InlineData("approve", PropertyStatus.Approved)]
    [InlineData("recycle", PropertyStatus.Recycled)]
    [InlineData("restore", PropertyStatus.Inbox)]
    [InlineData("APPROVE", PropertyStatus.Approved)]
    public void FromBulkAction_maps_correctly(string input, PropertyStatus expected)
    {
        PropertyStatusTransitions.FromBulkAction(input).Should().Be(expected);
    }

    [Fact]
    public void FromBulkAction_throws_on_unknown()
    {
        var act = () => PropertyStatusTransitions.FromBulkAction("nonsense");
        act.Should().Throw<ArgumentException>();
    }
}
