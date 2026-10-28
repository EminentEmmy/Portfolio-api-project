using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;
using PortfolioApi.Services;

using Xunit;

using static PortfolioApi.Data.Entities.Enums;

namespace PortfolioApi.Tests;

// Shared by the service tests: a fresh in-memory database per test.
internal static class TestDb
{
    public static AppDbContext Create()
    {
        // 1. Create a dedicated in-memory SQLite connection channel
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();

        // 2. Build the context options using the SQLite provider
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDbContext(options);

        // 3. Automatically build your structural tables and relationships inside RAM
        db.Database.EnsureCreated();

        return db;
    }


    public static async Task<(Portfolio Portfolio, Asset Asset)> SeedAsync(AppDbContext db)
    {
        var portfolio = new Portfolio
        {
            Name = "Retirement",
            OwnerEmail = "ada@example.com",
            BaseCurrency = "USD",
            CreatedAtUtc = DateTime.UtcNow
        };
        var asset = new Asset
        {
            Symbol = "ACME",
            Name = "Acme Corp",
            AssetType = AssetType.Stock,
            CurrentPrice = 50m,
            //PriceUpdatedAtUtc = DateTime.UtcNow
        };

        db.AddRange(portfolio, asset);
        await db.SaveChangesAsync();
        return (portfolio, asset);
    }

    public static Transaction NewTransaction(int portfolioId, int assetId, DateTime executedAtUtc) => new()
    {
        PortfolioId = portfolioId,
        AssetId = assetId,
        Type = TransactionType.Buy,
        Quantity = 1m,
        UnitPrice = 50m,
        Fee = 0m,
        ExecutedAtUtc = executedAtUtc
    };
}

public class PortfolioServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Create_with_a_bad_email_throws_a_validation_error()
    {
        await using var db = TestDb.Create();
        var service = new PortfolioService(db);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(new CreatePortfolioRequest("Retirement", "not-an-email", null), Ct));

        Assert.True(ex.Errors.ContainsKey("ownerEmail"));
    }

    [Fact]
    public async Task Create_lowercases_the_email_and_defaults_the_currency_to_USD()
    {
        await using var db = TestDb.Create();
        var service = new PortfolioService(db);

        var created = await service.CreateAsync(
            new CreatePortfolioRequest("Retirement", " Ada@Example.com ", null), Ct);

        Assert.Equal("ada@example.com", created.OwnerEmail);
        Assert.Equal("USD", created.BaseCurrency);
    }

    [Fact]
    public async Task Create_with_the_same_name_for_the_same_owner_throws_a_conflict()
    {
        await using var db = TestDb.Create();
        var service = new PortfolioService(db);
        var request = new CreatePortfolioRequest("Retirement", "ada@example.com", null);
        await service.CreateAsync(request, Ct);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request, Ct));

        Assert.Equal("Duplicate portfolio", ex.Title);
    }

    [Fact]
    public async Task Get_an_unknown_portfolio_throws_not_found()
    {
        await using var db = TestDb.Create();
        var service = new PortfolioService(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(999, Ct));

        Assert.Equal("Portfolio 999 does not exist.", ex.Message);
    }

    [Fact]
    public async Task Delete_is_blocked_when_the_portfolio_has_transactions()
    {
        await using var db = TestDb.Create();
        var (portfolio, asset) = await TestDb.SeedAsync(db);
        db.Transactions.Add(TestDb.NewTransaction(portfolio.Id, asset.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var service = new PortfolioService(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(portfolio.Id, Ct));

        Assert.Equal("Portfolio has transactions", ex.Title);
    }

    [Fact]
    public async Task Delete_removes_a_portfolio_without_transactions()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new PortfolioService(db);

        await service.DeleteAsync(portfolio.Id, Ct);

        Assert.False(await db.Portfolios.AnyAsync(p => p.Id == portfolio.Id));
    }

    [Fact]
    public async Task Transaction_history_page_2_is_newest_first()
    {
        await using var db = TestDb.Create();
        var (portfolio, asset) = await TestDb.SeedAsync(db);
        var start = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 25; i++)
            db.Transactions.Add(TestDb.NewTransaction(portfolio.Id, asset.Id, start.AddMinutes(i)));
        await db.SaveChangesAsync();
        var service = new PortfolioService(db);

        var page = await service.ListTransactionsAsync(
            portfolio.Id, new TransactionQuery(null, null, null, null, 2, 10), Ct);

        Assert.Equal(25, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
        // Ids 1..25 were added oldest first, so page 2 of the newest-first list is 15 down to 6.
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), page.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Transaction_history_rejects_a_bad_page_size(int pageSize)
    {
        await using var db = TestDb.Create();
        var service = new PortfolioService(db);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.ListTransactionsAsync(1, new TransactionQuery(null, null, null, null, 1, pageSize), Ct));

        Assert.True(ex.Errors.ContainsKey("pageSize"));
    }
}