using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using PortfolioApi.Common;
using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;

using static PortfolioApi.Data.Entities.Enums;

namespace PortfolioApi.Services;

public interface IAssetService
{
    Task<AssetResponse> CreateAsync(CreateAssetRequest request, CancellationToken ct);
    Task<List<AssetResponse>> ListAsync(string? type, string? search, CancellationToken ct);
    Task<AssetResponse> GetBySymbolAsync(string symbol, CancellationToken ct);
    Task<AssetResponse> UpdatePriceAsync(string symbol, UpdatePriceRequest request, CancellationToken ct);
}

public class AssetService : IAssetService
{
    private static readonly Regex SymbolPattern = new("^[A-Z0-9]{1,10}$", RegexOptions.Compiled);
    private readonly AppDbContext _db;

    public AssetService(AppDbContext db) => _db = db;

    public static string Normalize(string? symbol) => (symbol ?? "").Trim().ToUpperInvariant();

    private static bool TryParseType(string? value, out AssetType type) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out type) && Enum.IsDefined(type);

    private static ConflictException Duplicate(string symbol) =>
        new("Duplicate symbol", $"Asset '{symbol}' already exists.");

    public async Task<AssetResponse> CreateAsync(CreateAssetRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var symbol = Normalize(req.Symbol);
        if (!SymbolPattern.IsMatch(symbol))
            errors["symbol"] = new[] { "Symbol must be 1-10 characters, A-Z and 0-9 only." };

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            errors["name"] = new[] { "Name is required." };

        if (!TryParseType(req.AssetType, out var type))
            errors["assetType"] = new[] { "AssetType must be Stock, Bond or Fund." };

        if (req.CurrentPrice is null or <= 0)
            errors["currentPrice"] = new[] { "CurrentPrice must be greater than 0." };

        if (errors.Count > 0) throw new RequestValidationException(errors);

        if (await _db.Assets.AnyAsync(a => a.Symbol == symbol, ct))
            throw Duplicate(symbol);

        var asset = new Asset
        {
            Symbol = symbol,
            Name = name!,
            AssetType = type,
            CurrentPrice = MoneyRounding.ToPrice(req.CurrentPrice!.Value),
            PriceUpdatedAtUtc = DateTime.UtcNow
        };
        _db.Assets.Add(asset);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Duplicate(symbol); // two simultaneous creates slipped past AnyAsync
        }

        return ToResponse(asset);
    }

    public async Task<List<AssetResponse>> ListAsync(string? type, string? search, CancellationToken ct)
    {
        var query = _db.Assets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!TryParseType(type, out var t))
                throw new RequestValidationException("type", "Unknown asset type. Allowed values: Stock, Bond, Fund.");
            query = query.Where(a => a.AssetType == t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a => a.Symbol.Contains(s) || a.Name.Contains(s));
        }

        return await query
            .OrderBy(a => a.Symbol)
            .Select(a => new AssetResponse(a.Id, a.Symbol, a.Name, a.AssetType, a.CurrentPrice, a.PriceUpdatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<AssetResponse> GetBySymbolAsync(string symbol, CancellationToken ct)
    {
        var s = Normalize(symbol);
        var asset = await _db.Assets.AsNoTracking()
            .Where(a => a.Symbol == s)
            .Select(a => new AssetResponse(a.Id, a.Symbol, a.Name, a.AssetType, a.CurrentPrice, a.PriceUpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return asset ?? throw new NotFoundException($"Asset '{s}' does not exist.");
    }

    public async Task<AssetResponse> UpdatePriceAsync(string symbol, UpdatePriceRequest req, CancellationToken ct)
    {
        if (req.Price is null or <= 0)
            throw new RequestValidationException("price", "Price must be greater than 0.");

        var s = Normalize(symbol);
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Symbol == s, ct)
                    ?? throw new NotFoundException($"Asset '{s}' does not exist.");

        asset.CurrentPrice = MoneyRounding.ToPrice(req.Price.Value);
        asset.PriceUpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(asset);
    }

    private static AssetResponse ToResponse(Asset a) =>
        new(a.Id, a.Symbol, a.Name, a.AssetType, a.CurrentPrice, a.PriceUpdatedAtUtc);
}