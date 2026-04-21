using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaftAlerts.Application.Abstractions;
using DaftAlerts.Application.Dtos;
using DaftAlerts.Application.Mapping;
using DaftAlerts.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DaftAlerts.Api.Endpoints;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (IPropertyRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetStatsAsync(ct)))
            .WithTags("Stats");
        return app;
    }
}

public static class PresetsEndpoints
{
    public static IEndpointRouteBuilder MapPresetsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/presets").WithTags("Presets");

        group.MapGet("", async (IFilterPresetRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetAllAsync(ct);
            return Results.Ok(items.Select(p => p.ToDto()).ToArray());
        });

        group.MapPost("", CreateAsync);
        group.MapPut("{id:guid}", UpdateAsync);
        group.MapDelete("{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<Results<Created<FilterPresetDto>, ValidationProblem>> CreateAsync(
        UpsertFilterPresetDto dto,
        IFilterPresetRepository repo,
        IUnitOfWork uow,
        IValidator<UpsertFilterPresetDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            return TypedResults.ValidationProblem(validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var preset = new FilterPreset { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        preset.ApplyFrom(dto);

        await repo.AddAsync(preset, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/presets/{preset.Id}", preset.ToDto());
    }

    private static async Task<Results<Ok<FilterPresetDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        UpsertFilterPresetDto dto,
        IFilterPresetRepository repo,
        IUnitOfWork uow,
        IValidator<UpsertFilterPresetDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            return TypedResults.ValidationProblem(validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null) return TypedResults.NotFound();

        existing.ApplyFrom(dto);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(existing.ToDto());
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, IFilterPresetRepository repo, IUnitOfWork uow, CancellationToken ct)
    {
        var removed = await repo.RemoveAsync(id, ct);
        if (!removed) return TypedResults.NotFound();
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
