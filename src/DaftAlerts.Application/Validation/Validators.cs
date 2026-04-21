using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Domain.ValueObjects;
using FluentValidation;

namespace DaftAlerts.Application.Validation;

public sealed class PropertyQueryValidator : AbstractValidator<PropertyQuery>
{
    private static readonly Regex RoutingKeyPattern = new(
        @"^[ADCEFHKNPRTVWXY]\d{2}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlySet<string> AllowedPropertyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "House", "Apartment", "Studio", "Shared", "Other"
    };

    public PropertyQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.MinBeds).InclusiveBetween(0, 50).When(q => q.MinBeds.HasValue);
        RuleFor(q => q.MaxBeds).InclusiveBetween(0, 50).When(q => q.MaxBeds.HasValue);
        RuleFor(q => q.MinBaths).InclusiveBetween(0, 50).When(q => q.MinBaths.HasValue);

        RuleFor(q => q.MinPrice).GreaterThanOrEqualTo(0).When(q => q.MinPrice.HasValue);
        RuleFor(q => q.MaxPrice).GreaterThanOrEqualTo(0).When(q => q.MaxPrice.HasValue);

        RuleFor(q => q).Must(q => !q.MinBeds.HasValue || !q.MaxBeds.HasValue || q.MinBeds <= q.MaxBeds)
            .WithMessage("minBeds must be <= maxBeds.");
        RuleFor(q => q).Must(q => !q.MinPrice.HasValue || !q.MaxPrice.HasValue || q.MinPrice <= q.MaxPrice)
            .WithMessage("minPrice must be <= maxPrice.");

        RuleForEach(q => q.RoutingKeys!).Must(rk => RoutingKeyPattern.IsMatch(rk))
            .WithMessage("Routing keys must match the Irish Eircode routing-key format, e.g. 'D02'.")
            .When(q => q.RoutingKeys is not null);

        RuleForEach(q => q.PropertyTypes!).Must(pt => AllowedPropertyTypes.Contains(pt))
            .WithMessage("Property type must be one of: House, Apartment, Studio, Shared, Other.")
            .When(q => q.PropertyTypes is not null);

        RuleFor(q => q.BerMin!).Must(b => BerRank.IsKnown(b)).When(q => !string.IsNullOrWhiteSpace(q.BerMin))
            .WithMessage("berMin must be one of A1..G or Exempt.");
    }
}

public sealed class UpdatePropertyValidator : AbstractValidator<UpdatePropertyDto>
{
    private static readonly IReadOnlySet<string> AllowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "inbox", "approved", "recycled"
    };

    public UpdatePropertyValidator()
    {
        RuleFor(x => x.Status!).Must(s => AllowedStatuses.Contains(s)).When(x => x.Status is not null)
            .WithMessage("status must be one of: inbox, approved, recycled.");
        RuleFor(x => x.Notes!).MaximumLength(4000).When(x => x.Notes is not null);
    }
}

public sealed class BulkActionValidator : AbstractValidator<BulkActionDto>
{
    private static readonly IReadOnlySet<string> AllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "approve", "recycle", "restore"
    };

    public BulkActionValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().Must(ids => ids.Count <= 500)
            .WithMessage("Up to 500 ids per bulk operation.");
        RuleFor(x => x.Action).Must(a => AllowedActions.Contains(a))
            .WithMessage("action must be one of: approve, recycle, restore.");
    }
}

public sealed class UpsertFilterPresetValidator : AbstractValidator<UpsertFilterPresetDto>
{
    public UpsertFilterPresetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MinBeds).InclusiveBetween(0, 50).When(x => x.MinBeds.HasValue);
        RuleFor(x => x.MaxBeds).InclusiveBetween(0, 50).When(x => x.MaxBeds.HasValue);
        RuleFor(x => x).Must(x => !x.MinBeds.HasValue || !x.MaxBeds.HasValue || x.MinBeds <= x.MaxBeds)
            .WithMessage("minBeds must be <= maxBeds.");
        RuleFor(x => x).Must(x => !x.MinPrice.HasValue || !x.MaxPrice.HasValue || x.MinPrice <= x.MaxPrice)
            .WithMessage("minPrice must be <= maxPrice.");
        RuleFor(x => x.BerMin!).Must(b => BerRank.IsKnown(b)).When(x => !string.IsNullOrWhiteSpace(x.BerMin));
        RuleForEach(x => x.RoutingKeys).Matches(@"^[ADCEFHKNPRTVWXY]\d{2}$");
        RuleFor(x => x.RoutingKeys).NotNull();
        RuleFor(x => x.PropertyTypes).NotNull();
    }
}
