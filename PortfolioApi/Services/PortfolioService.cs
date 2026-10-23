using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.Data.Entities;
using PortfolioApi.DTOs;
using PortfolioApi.Exceptions;

namespace PortfolioApi.Services;

public interface IPortfolioService
{
    Task<PortfolioResponse> CreateAsync(CreatePortfolioRequest request, CancellationToken ct);
    Task<List<PortfolioResponse>> ListAsync(string? ownerEmail, CancellationToken ct);
    Task<PortfolioResponse> GetAsync(int id, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public class PortfolioService : IPortfolioService
{
    private readonly AppDbContext _db;
    public PortfolioService(AppDbContext db) => _db = db;

    private static bool IsValidEmail(string email) =>
        MailAddress.TryCreate(email, out var addr) && addr.Address == email && email.Contains('.');

    private static ConflictException Duplicate(string name) =>
        new("Duplicate portfolio", $"Portfolio '{name}' already exists for this owner.");

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
}
