using Microsoft.AspNetCore.Mvc;
using PortfolioApi.DTOs;
using PortfolioApi.Services;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/portfolios")]
public class PortfoliosController : ControllerBase
{
    private readonly IPortfolioService _portfolios;
    public PortfoliosController(IPortfolioService portfolios) => _portfolios = portfolios;

    [HttpPost]
    public async Task<ActionResult<PortfolioResponse>> Create(CreatePortfolioRequest request, CancellationToken ct)
    {
        var created = await _portfolios.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<List<PortfolioResponse>>> List([FromQuery] string? ownerEmail, CancellationToken ct) =>
        Ok(await _portfolios.ListAsync(ownerEmail, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PortfolioResponse>> Get(int id, CancellationToken ct) =>
        Ok(await _portfolios.GetAsync(id, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _portfolios.DeleteAsync(id, ct);
        return NoContent();
    }
}

