using DaftAlerts.Domain.ValueObjects;
using DaftAlerts.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DaftAlerts.Infrastructure.Tests.Persistence;

public static class TestDbContextFactory
{
    public static AppDbContext Create(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        connection.CreateFunction(
            "berrank",
            (string? ber) => BerRank.Rank(ber),
            isDeterministic: true
        );

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
