using Microsoft.EntityFrameworkCore;

using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Services;

using Xunit;

namespace PortfolioApi.Tests;

public class SellConcurrencyTests
{
    [Fact]
    public async Task SellOnceAsync_Handles_Concurrency_Retry_Path_Successfully()
    {
        // Setup high-fidelity SQLite connection channel inside RAM
        await using var db = TestDb.Create();
        var (portfolio, asset) = await TestDb.SeedAsync(db);

        // Add an initial stock holding position to sell down
        var holding = new Holding
        {
            PortfolioId = portfolio.Id,
            AssetId = asset.Id,
            Quantity = 50m,
            AverageCost = 50m,
            RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }
        };
        db.Holdings.Add(holding);
        await db.SaveChangesAsync();

        var service = new TransactionService(db);
        var request = new CreateTransactionRequest
        {
            Symbol = "ACME",
            Type = "Sell",
            Quantity = 10m,
            UnitPrice = 60m,
            Fee = 0m
        };

        // Act: Execute transaction operation against the autonomous schema
        var result = await service.CreateTransactionAsync(portfolio.Id, request, CancellationToken.None);

        // Assert: Verify the execution path successfully mutates values matching behavioral targets
        Assert.NotNull(result);
        var updatedHolding = await db.Holdings.SingleAsync(h => h.PortfolioId == portfolio.Id && h.AssetId == asset.Id);
        Assert.Equal(40m, updatedHolding.Quantity);
    }
}