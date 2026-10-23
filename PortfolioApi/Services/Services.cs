using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Common;
using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;
using System.Globalization;
using static PortfolioApi.Data.Entities.Enums;

namespace PortfolioApi.Services;

public interface ITransactionService
{
    Task<TransactionResponse> CreateTransactionAsync(
        int portfolioId, CreateTransactionRequest request, CancellationToken ct);
}

public class TransactionService : ITransactionService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _dbContext;

    public TransactionService(AppDbContext db) => _dbContext = db;

    public async Task<TransactionResponse> CreateTransactionAsync(
        int portfolioId, CreateTransactionRequest request, CancellationToken ct)
    {
        // 1. Validate (400)
        var errors = new Dictionary<string, string[]>();

        var symbol = (request.Symbol ?? "").Trim().ToUpperInvariant();
        if (symbol.Length == 0)
            errors["symbol"] = new[] { "Symbol is required." };

        TransactionType type = default;
        var typeText = request.Type?.Trim();
        if (string.Equals(typeText, "Buy", StringComparison.OrdinalIgnoreCase)) type = TransactionType.Buy;
        else if (string.Equals(typeText, "Sell", StringComparison.OrdinalIgnoreCase)) type = TransactionType.Sell;
        else errors["type"] = new[] { "Type must be Buy or Sell." };

        if (request.Quantity <= 0)
            errors["quantity"] = new[] { "Quantity must be greater than 0." };

        var fee = request.Fee ?? 0m;
        if (fee < 0)
            errors["fee"] = new[] { "Fee must be 0 or greater." };

        if (request.UnitPrice is <= 0)
            errors["unitPrice"] = new[] { "UnitPrice must be greater than 0." };

        if (errors.Count > 0) throw new RequestValidationException(errors);

        // 2. Existence checks (404)
        var portfolioExists = await _dbContext.Portfolios.AsNoTracking()
            .AnyAsync(p => p.Id == portfolioId, ct);
        if (!portfolioExists)
            throw new NotFoundException($"Portfolio {portfolioId} does not exist.");

        var asset = await _dbContext.Assets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Symbol == symbol, ct)
            ?? throw new NotFoundException($"Asset '{symbol}' does not exist.");

        var unitPrice = request.UnitPrice ?? asset.CurrentPrice;

        // 3. Apply, retrying if another request changed the holding first
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = type == TransactionType.Buy
                    ? await BuyOnceAsync(portfolioId, asset, request.Quantity, unitPrice, fee, ct)
                    : await SellOnceAsync(portfolioId, asset, request.Quantity, unitPrice, fee, ct);

                if (response is not null)
                    return response;

                _dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new ConflictException(
            "Concurrent change",
            $"The holding for {asset.Symbol} in portfolio {portfolioId} was changed by another request. Please retry.");
    }

    private async Task<TransactionResponse?> BuyOnceAsync(
        int portfolioId, Asset asset, decimal quantity, decimal unitPrice, decimal fee, CancellationToken ct)
    {
        await using var dbTx = await _dbContext.Database.BeginTransactionAsync(ct);

        var holding = await _dbContext.Holdings
            .FirstOrDefaultAsync(h => h.PortfolioId == portfolioId && h.AssetId == asset.Id, ct);

        var current = holding is null
            ? PositionState.Empty
            : new PositionState(holding.Quantity, holding.AverageCost, holding.RealizedPnl);

        var trade = PositionCalculator.Buy(current, quantity, unitPrice, fee);

        if (holding is null)
        {
            holding = new Holding { PortfolioId = portfolioId, AssetId = asset.Id };
            _dbContext.Holdings.Add(holding);
        }

        holding.Quantity = trade.Position.Quantity;
        holding.AverageCost = trade.Position.AverageCost;
        holding.RealizedPnl = trade.Position.RealizedPnl;

        var row = NewTransactionRow(portfolioId, asset.Id, quantity, unitPrice, fee, TransactionType.Buy);
        _dbContext.Transactions.Add(row);

        await _dbContext.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return ToResponse(row, asset.Symbol, trade);
    }

    private async Task<TransactionResponse?> SellOnceAsync(
        int portfolioId, Asset asset, decimal quantity, decimal unitPrice, decimal fee, CancellationToken ct)
    {
        await using var dbTx = await _dbContext.Database.BeginTransactionAsync(ct);

        var snapshot = await _dbContext.Holdings.AsNoTracking()
            .Where(h => h.PortfolioId == portfolioId && h.AssetId == asset.Id)
            .Select(h => new { h.Quantity, h.AverageCost, h.RealizedPnl, h.RowVersion })
            .FirstOrDefaultAsync(ct);

        var held = snapshot?.Quantity ?? 0m;   // never held = 0 held
        if (snapshot is null || quantity > held)
        {
            throw new ConflictException(
                "Insufficient holdings",
                $"Cannot sell {Format(quantity)} {asset.Symbol}; portfolio {portfolioId} holds {Format(held)}.");
        }

        var current = new PositionState(snapshot.Quantity, snapshot.AverageCost, snapshot.RealizedPnl);
        var trade = PositionCalculator.Sell(current, quantity, unitPrice, fee);

        var newQuantity = trade.Position.Quantity;
        var newAverageCost = trade.Position.AverageCost;
        var newRealizedPnl = trade.Position.RealizedPnl;
        var rowVersion = snapshot.RowVersion;

        var affected = await _dbContext.Holdings
            .Where(h => h.PortfolioId == portfolioId
                     && h.AssetId == asset.Id
                     && h.Quantity >= quantity
                     && h.RowVersion == rowVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(h => h.Quantity, newQuantity)
                .SetProperty(h => h.AverageCost, newAverageCost)
                .SetProperty(h => h.RealizedPnl, newRealizedPnl), ct);

        if (affected == 0)
            return null;   // lost the race; leaving rolls back, caller retries and re-reads

        var row = NewTransactionRow(portfolioId, asset.Id, quantity, unitPrice, fee, TransactionType.Sell);
        _dbContext.Transactions.Add(row);
        await _dbContext.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);   // holding update + transaction row succeed or fail together

        return ToResponse(row, asset.Symbol, trade);
    }

    // --- helpers ---
    private static Transaction NewTransactionRow(
        int portfolioId, int assetId, decimal quantity, decimal unitPrice, decimal fee, TransactionType type)
    {
        var now = DateTime.UtcNow;
        var seconds = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

        return new Transaction
        {
            PortfolioId = portfolioId,
            AssetId = assetId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Fee = fee,
            Type = type,
            ExecutedAtUtc = seconds
        };
    }

    private static TransactionResponse ToResponse(Transaction row, string symbol, TradeResult trade) => new()
    {
        Id = row.Id,
        PortfolioId = row.PortfolioId,
        Symbol = symbol,
        Type = row.Type.ToString(),
        Quantity = row.Quantity,
        UnitPrice = row.UnitPrice,
        Fee = row.Fee,
        GrossAmount = MoneyRounding.ToMoney(trade.GrossAmount),
        NetAmount = MoneyRounding.ToMoney(trade.NetAmount),
        RealizedPnl = row.Type == TransactionType.Sell ? MoneyRounding
        .ToMoney(trade.RealizedPnl ?? 0) : 0,
        ExecutedAtUtc = row.ExecutedAtUtc
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    internal static string Format(decimal value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}