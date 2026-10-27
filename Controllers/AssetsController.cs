using Microsoft.AspNetCore.Mvc;

using PortfolioApi.DTOs;
using PortfolioApi.Services;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/assets")]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assets;

    public AssetsController(IAssetService assets) => _assets = assets;

    [HttpPost]
    public async Task<ActionResult<AssetResponse>> Create(CreateAssetRequest request, CancellationToken ct)
    {
        var created = await _assets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetBySymbol), new { symbol = created.Symbol }, created);
    }

    [HttpGet]
    public async Task<ActionResult<List<AssetResponse>>> List(
        [FromQuery] string? type, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await _assets.ListAsync(type, search, ct));

    [HttpGet("{symbol}")]
    public async Task<ActionResult<AssetResponse>> GetBySymbol(string symbol, CancellationToken ct) =>
        Ok(await _assets.GetBySymbolAsync(symbol, ct));

    [HttpPut("{symbol}/price")]
    public async Task<ActionResult<AssetResponse>> UpdatePrice(
        string symbol, UpdatePriceRequest request, CancellationToken ct) =>
        Ok(await _assets.UpdatePriceAsync(symbol, request, ct));
}