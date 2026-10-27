using Microsoft.EntityFrameworkCore;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;
using PortfolioApi.Services;
using Xunit;

namespace PortfolioApi.Tests;

// These tests use the in-memory provider, which cannot run ExecuteUpdateAsync.
// So they cover buys and every path that fails before the guarded sell update.
// A successful sell and the concurrency retry need a SQLite or SQL Server test later.
public class TransactionServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static CreateTransactionRequest Request(
        string type, decimal quantity, decimal? unitPrice = null, decimal? fee = null, string symbol = "ACME") =>
        new()
        {
            Symbol = symbol,
            Type = type,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Fee = fee
        };

    [Fact]
    public async Task Buy_returns_the_gross_and_net_amounts()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var result = await service.CreateTransactionAsync(portfolio.Id, Request("Buy", 100m, 50m, 10m), Ct);

        Assert.Equal("ACME", result.Symbol);
        Assert.Equal(5000m, result.GrossAmount);
        Assert.Equal(5010m, result.NetAmount);   // buy: gross + fee
    }

    [Fact]
    public async Task Buy_without_a_unit_price_uses_the_asset_price()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var result = await service.CreateTransactionAsync(portfolio.Id, Request("Buy", 10m), Ct);

        Assert.Equal(50m, result.UnitPrice);
    }

    [Fact]
    public async Task Two_buys_blend_the_average_cost()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        await service.CreateTransactionAsync(portfolio.Id, Request("Buy", 100m, 50m, 10m), Ct);
        await service.CreateTransactionAsync(portfolio.Id, Request("Buy", 100m, 54m, 10m), Ct);

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal(200m, holding.Quantity);
        Assert.Equal(52.10m, holding.AverageCost);
    }

    [Fact]
    public async Task Selling_more_than_held_returns_409_naming_both_quantities()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);
        await service.CreateTransactionAsync(portfolio.Id, Request("Buy", 100m, 50m, 0m), Ct);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateTransactionAsync(portfolio.Id, Request("Sell", 500m, 60m, 0m), Ct));

        Assert.Equal("Insufficient holdings", ex.Title);
        Assert.Equal($"Cannot sell 500 ACME; portfolio {portfolio.Id} holds 100.", ex.Message);
    }

    [Fact]
    public async Task Selling_an_asset_never_held_reports_zero_held()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateTransactionAsync(portfolio.Id, Request("Sell", 1m, 60m, 0m), Ct));

        Assert.Equal($"Cannot sell 1 ACME; portfolio {portfolio.Id} holds 0.", ex.Message);
    }

    [Fact]
    public async Task A_zero_quantity_is_a_validation_error()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateTransactionAsync(portfolio.Id, Request("Buy", 0m, 50m, 0m), Ct));

        Assert.Equal("Quantity must be greater than 0.", ex.Errors["quantity"][0]);
    }

    [Fact]
    public async Task An_unknown_type_is_a_validation_error()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateTransactionAsync(portfolio.Id, Request("Hold", 1m, 50m, 0m), Ct));

        Assert.True(ex.Errors.ContainsKey("type"));
    }

    [Fact]
    public async Task An_unknown_symbol_throws_not_found()
    {
        await using var db = TestDb.Create();
        var (portfolio, _) = await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateTransactionAsync(portfolio.Id, Request("Buy", 1m, 50m, 0m, symbol: "ZZZZ"), Ct));

        Assert.Equal("Asset 'ZZZZ' does not exist.", ex.Message);
    }

    [Fact]
    public async Task An_unknown_portfolio_throws_not_found()
    {
        await using var db = TestDb.Create();
        await TestDb.SeedAsync(db);
        var service = new TransactionService(db);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateTransactionAsync(999, Request("Buy", 1m, 50m, 0m), Ct));

        Assert.Equal("Portfolio 999 does not exist.", ex.Message);
    }
}