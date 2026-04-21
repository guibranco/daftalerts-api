using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Application.Mapping;
using DaftAlerts.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DaftAlerts.Api.Endpoints;

public static class PropertiesEndpoints
{
    public static IEndpointRouteBuilder MapPropertiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/properties").WithTags("Properties");

        group.MapGet("", ListAsync);
        group.MapGet("{id:guid}", GetAsync);
        group.MapPatch("{id:guid}", PatchAsync);
        group.MapPost("bulk", BulkAsync);

        return app;
    }

    private static async Task<Results<Ok<PagedResult<PropertyDto>>, ValidationProblem>> ListAsync(
        HttpContext ctx,
        IPropertyRepository repo,
        IValidator<PropertyQuery> validator,
        CancellationToken ct,
        string status = "inbox",
        int page = 1,
        int pageSize = 24,
        string? search = null,
        string? routingKeys = null,
        int? minBeds = null,
        int? maxBeds = null,
        int? minBaths = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        string? propertyTypes = null,
        string? berMin = null,
        string sortBy = "receivedAt",
        string sortDir = "desc")
    {
        var query = new PropertyQuery(
            Status: PropertyMappings.ParseStatusString(status),
            Page: page,
            PageSize: pageSize,
            Search: search,
            RoutingKeys: SplitCsv(routingKeys),
            MinBeds: minBeds,
            MaxBeds: maxBeds,
            MinBaths: minBaths,
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            PropertyTypes: SplitCsv(propertyTypes),
            BerMin: berMin,
            SortBy: ParseSortField(sortBy),
            SortDir: ParseSortDir(sortDir));

        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid)
        {
            var dict = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return TypedResults.ValidationProblem(dict);
        }

        var result = await repo.QueryAsync(query, ct);
        var dtos = result.Items.Select(p => p.ToDto()).ToArray();
        return TypedResults.Ok(new PagedResult<PropertyDto>(dtos, result.Total, result.Page, result.PageSize));
    }

    private static async Task<Results<Ok<PropertyDto>, NotFound>> GetAsync(
        Guid id, IPropertyRepository repo, CancellationToken ct)
    {
        var property = await repo.GetByIdAsync(id, ct);
        return property is null ? TypedResults.NotFound() : TypedResults.Ok(property.ToDto());
    }

    private static async Task<Results<Ok<PropertyDto>, NotFound, ValidationProblem>> PatchAsync(
        Guid id,
        UpdatePropertyDto dto,
        IPropertyRepository repo,
        IUnitOfWork uow,
        IClock clock,
        IValidator<UpdatePropertyDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            var dict = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return TypedResults.ValidationProblem(dict);
        }

        var property = await repo.GetByIdAsync(id, ct);
        if (property is null) return TypedResults.NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            var newStatus = PropertyMappings.ParseStatusString(dto.Status);
            PropertyStatusTransitions.Transition(property, newStatus, clock);
        }
        if (dto.Notes is not null)
        {
            property.Notes = dto.Notes;
            property.UpdatedAt = clock.UtcNow;
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(property.ToDto());
    }

    private static async Task<Results<Ok<BulkActionResultDto>, ValidationProblem>> BulkAsync(
        BulkActionDto dto,
        IPropertyRepository repo,
        IUnitOfWork uow,
        IValidator<BulkActionDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            var dict = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return TypedResults.ValidationProblem(dict);
        }

        var status = PropertyStatusTransitions.FromBulkAction(dto.Action);
        var updated = await repo.UpdateStatusAsync(dto.Ids, status, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(new BulkActionResultDto(updated));
    }

    // --- helpers --------------------------------------------------------

    private static IReadOnlyList<string>? SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static PropertySortField ParseSortField(string s) => s.ToLowerInvariant() switch
    {
        "price" => PropertySortField.Price,
        "beds" => PropertySortField.Beds,
        _ => PropertySortField.ReceivedAt
    };

    private static SortDirection ParseSortDir(string s) =>
        string.Equals(s, "asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Asc : SortDirection.Desc;
}
