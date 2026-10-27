using System.Globalization;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Common;
using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;
using static PortfolioApi.Data.Entities.Enums;

namespace PortfolioApi.Services;

public interface IPortfolioService
{
    Task<PortfolioResponse> CreateAsync(CreatePortfolioRequest request, CancellationToken ct);
    Task<List<PortfolioResponse>> ListAsync(string? ownerEmail, CancellationToken ct);
    Task<PortfolioResponse> GetAsync(int id, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    Task<List<HoldingResponse>> GetHoldingsAsync(int id, CancellationToken ct);
    Task<PortfolioSummaryResponse> GetSummaryAsync(int id, CancellationToken ct);
    Task<PagedResponse<TransactionListItem>> ListTransactionsAsync(int id, TransactionQuery query, CancellationToken ct);
}

public class PortfolioService : IPortfolioService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    public PortfolioService(AppDbContext db) => _db = db;

    private static bool IsValidEmail(string email) =>
        MailAddress.TryCreate(email, out var addr) && addr.Address == email && email.Contains('.');

    private static ConflictException Duplicate(string name) =>
        new("Duplicate portfolio", $"Portfolio '{name}' already exists for this owner.");

    private static NotFoundException PortfolioNotFound(int id) =>
        new($"Portfolio {id} does not exist.");

    public async Task<PortfolioResponse> CreateAsync(CreatePortfolioRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            errors["name"] = new[] { "Name is required." };

        var email = req.OwnerEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
            errors["ownerEmail"] = new[] { "OwnerEmail must be a valid email address." };

        var currency = string.IsNullOrWhiteSpace(req.BaseCurrency) ? "USD" : req.BaseCurrency.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            errors["baseCurrency"] = new[] { "BaseCurrency must be a 3-letter code." };

        if (errors.Count > 0) throw new RequestValidationException(errors);

        if (await _db.Portfolios.AnyAsync(p => p.OwnerEmail == email && p.Name == name, ct))
            throw Duplicate(name!);

        var portfolio = new Portfolio
        {
            Name = name!,
            OwnerEmail = email!,
            BaseCurrency = currency,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Portfolios.Add(portfolio);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Duplicate(name!);
        }

        return ToResponse(portfolio);
    }

    public async Task<List<PortfolioResponse>> ListAsync(string? ownerEmail, CancellationToken ct)
    {
        var email = ownerEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
            throw new RequestValidationException("ownerEmail", "ownerEmail is required and must be a valid email address.");

        return await _db.Portfolios.AsNoTracking()
            .Where(p => p.OwnerEmail == email)
            .OrderBy(p => p.Name)
            .Select(p => new PortfolioResponse(p.Id, p.Name, p.OwnerEmail, p.BaseCurrency, p.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<PortfolioResponse> GetAsync(int id, CancellationToken ct)
    {
        var p = await _db.Portfolios.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new PortfolioResponse(x.Id, x.Name, x.OwnerEmail, x.BaseCurrency, x.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return p ?? throw new NotFoundException($"Portfolio {id} does not exist.");
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var portfolio = await _db.Portfolios.FirstOrDefaultAsync(p => p.Id == id, ct)
                        ?? throw new NotFoundException($"Portfolio {id} does not exist.");

        if (await _db.Transactions.AnyAsync(t => t.PortfolioId == id, ct))
            throw new ConflictException("Portfolio has transactions",
                $"Portfolio {id} has transactions and cannot be deleted.");

        _db.Portfolios.Remove(portfolio);
        await _db.SaveChangesAsync(ct);
    }

    private static PortfolioResponse ToResponse(Portfolio p) =>
        new(p.Id, p.Name, p.OwnerEmail, p.BaseCurrency, p.CreatedAtUtc);

    // =================================================================
    // Holdings
    // =================================================================

    public async Task<List<HoldingResponse>> GetHoldingsAsync(int id, CancellationToken ct)
    {
        var data = await LoadPositionsAsync(id, ct);   // exactly one SELECT

        // Values are calculated from raw decimals and rounded only for output (rule 7).
        return data.Rows
            .Where(r => r.Quantity > 0)
            .Select(r => new
            {
                Row = r,
                Valuation = PositionCalculator.Value(r.Quantity, r.AverageCost, r.CurrentPrice)
            })
            .OrderByDescending(x => x.Valuation.MarketValue)     // highest market value first
            .ThenBy(x => x.Row.Symbol, StringComparer.Ordinal)   // stable order on ties
            .Select(x => new HoldingResponse(
                x.Row.Symbol,
                x.Row.Quantity,
                x.Row.AverageCost,                                // stored to 4 dp
                MoneyRounding.ToMoney(x.Valuation.CostBasis),
                x.Row.CurrentPrice,
                MoneyRounding.ToMoney(x.Valuation.MarketValue),
                MoneyRounding.ToMoney(x.Valuation.UnrealizedPnl),
                MoneyRounding.ToMoney(x.Valuation.UnrealizedPnlPercent),
                MoneyRounding.ToMoney(x.Row.RealizedPnl)))
            .ToList();
    }

    // =================================================================
    // Summary
    // =================================================================

    public async Task<PortfolioSummaryResponse> GetSummaryAsync(int id, CancellationToken ct)
    {
        var data = await LoadPositionsAsync(id, ct);   // exactly one SELECT

        // Totals cover OPEN holdings only.
        var open = data.Rows
            .Where(r => r.Quantity > 0)
            .Select(r => new
            {
                r.AssetType,
                Valuation = PositionCalculator.Value(r.Quantity, r.AverageCost, r.CurrentPrice)
            })
            .ToList();

        // Sum the raw values and round only at the end.
        var totalMarketValue = open.Sum(x => x.Valuation.MarketValue);
        var totalCostBasis = open.Sum(x => x.Valuation.CostBasis);
        var unrealized = totalMarketValue - totalCostBasis;

        // Realized P&L sums ALL holding rows, including fully sold ones.
        var realized = data.Rows.Sum(r => r.RealizedPnl);

        var allocation = open
            .GroupBy(x => x.AssetType)
            .Select(g => new { AssetType = g.Key, MarketValue = g.Sum(x => x.Valuation.MarketValue) })
            .OrderByDescending(x => x.MarketValue)
            .Select(x => new AllocationResponse(
                x.AssetType,
                MoneyRounding.ToMoney(x.MarketValue),
                MoneyRounding.ToMoney(Percent(x.MarketValue, totalMarketValue))))
            .ToList();

        return new PortfolioSummaryResponse(
            id,
            data.Name,
            data.BaseCurrency,
            open.Count,
            MoneyRounding.ToMoney(totalMarketValue),
            MoneyRounding.ToMoney(totalCostBasis),
            MoneyRounding.ToMoney(unrealized),
            MoneyRounding.ToMoney(Percent(unrealized, totalCostBasis)),
            MoneyRounding.ToMoney(realized),
            MoneyRounding.ToMoney(unrealized + realized),
            allocation);
    }

    private static decimal Percent(decimal part, decimal whole) =>
        whole == 0 ? 0m : part / whole * 100m;

    // One SELECT: the portfolio with all of its holdings and each holding's asset, read-only.
    // If the portfolio does not exist the result is null -> 404, with no second query.
    private async Task<PortfolioPositions> LoadPositionsAsync(int id, CancellationToken ct)
    {
        var data = await _db.Portfolios.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PortfolioPositions(
                p.Name,
                p.BaseCurrency,
                p.Holdings
                    .Select(h => new PositionRow(
                        h.Asset.Symbol,
                        h.Asset.AssetType,
                        h.Quantity,
                        h.AverageCost,
                        h.RealizedPnl,
                        h.Asset.CurrentPrice))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return data ?? throw PortfolioNotFound(id);
    }

    // =================================================================
    // Transaction history (paged, newest first)
    // =================================================================

    public async Task<PagedResponse<TransactionListItem>> ListTransactionsAsync(
        int id, TransactionQuery q, CancellationToken ct)
    {
        // ---- 400: validate every query parameter ----
        var errors = new Dictionary<string, string[]>();

        var symbol = string.IsNullOrWhiteSpace(q.Symbol) ? null : q.Symbol.Trim().ToUpperInvariant();

        TransactionType? type = null;
        if (!string.IsNullOrWhiteSpace(q.Type))
        {
            type = ParseTransactionType(q.Type);
            if (type is null)
                errors["type"] = new[] { "Type must be Buy or Sell." };
        }

        var fromDay = ParseDay(q.From, "from", errors);
        var toDay = ParseDay(q.To, "to", errors);
        if (fromDay is not null && toDay is not null && fromDay > toDay)
            errors["from"] = new[] { "From must not be after To." };

        if (q.Page < 1)
            errors["page"] = new[] { "Page must be 1 or greater." };
        if (q.PageSize < 1 || q.PageSize > MaxPageSize)
            errors["pageSize"] = new[] { $"PageSize must be between 1 and {MaxPageSize}." };

        if (errors.Count > 0) throw new RequestValidationException(errors);

        // ---- 404 ----
        var portfolioExists = await _db.Portfolios.AsNoTracking().AnyAsync(p => p.Id == id, ct);
        if (!portfolioExists) throw PortfolioNotFound(id);

        // ---- filters ----
        var query = _db.Transactions.AsNoTracking().Where(t => t.PortfolioId == id);

        if (symbol is not null)
            query = query.Where(t => t.Asset.Symbol == symbol);
        if (type is { } typeValue)
            query = query.Where(t => t.Type == typeValue);
        if (fromDay is { } start)
            query = query.Where(t => t.ExecutedAtUtc >= start);
        if (toDay is { } end)
        {
            var endExclusive = end.AddDays(1);              // "to" includes the whole day
            query = query.Where(t => t.ExecutedAtUtc < endExclusive);
        }

        var totalCount = await query.CountAsync(ct);

        var skip = (int)Math.Min((long)(q.Page - 1) * q.PageSize, int.MaxValue);

        var rows = await query
            .OrderByDescending(t => t.ExecutedAtUtc)
            .ThenByDescending(t => t.Id)                    // ExecutedAtUtc is truncated to seconds, so break ties by Id
            .Skip(skip)
            .Take(q.PageSize)
            .Select(t => new
            {
                t.Id,
                t.PortfolioId,
                Symbol = t.Asset.Symbol,
                t.Type,
                t.Quantity,
                t.UnitPrice,
                t.Fee,
                t.ExecutedAtUtc
            })
            .ToListAsync(ct);

        // Amounts are computed from the frozen row values and rounded only for output.
        var items = rows.Select(t =>
        {
            var gross = t.Quantity * t.UnitPrice;
            var net = t.Type == TransactionType.Buy ? gross + t.Fee : gross - t.Fee;

            return new TransactionListItem(
                t.Id,
                t.PortfolioId,
                t.Symbol,
                t.Type,
                t.Quantity,
                t.UnitPrice,
                t.Fee,
                MoneyRounding.ToMoney(gross),
                MoneyRounding.ToMoney(net),
                DateTime.SpecifyKind(t.ExecutedAtUtc, DateTimeKind.Utc));
        }).ToList();

        return new PagedResponse<TransactionListItem>(items, q.Page, q.PageSize, totalCount);
    }

    private static TransactionType? ParseTransactionType(string raw)
    {
        var value = raw.Trim();
        foreach (var candidate in Enum.GetValues<TransactionType>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;   // rejects numbers such as "1" and unknown names
    }

    // Accepts "2026-10-31" or a full ISO timestamp; only the UTC date is used.
    private static DateTime? ParseDay(string? raw, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.Date;

        errors[field] = new[] { $"{field} must be a valid date, for example 2026-10-31." };
        return null;
    }
}

// Read-only shapes used by LoadPositionsAsync.
internal sealed record PositionRow(
    string Symbol,
    AssetType AssetType,
    decimal Quantity,
    decimal AverageCost,
    decimal RealizedPnl,
    decimal CurrentPrice);

internal sealed record PortfolioPositions(
    string Name,
    string BaseCurrency,
    List<PositionRow> Rows);