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

    // GET /api/portfolios/{id}/holdings
    [HttpGet("{id:int}/holdings")]
    public async Task<ActionResult<List<HoldingResponse>>> Holdings(int id, CancellationToken ct) =>
        Ok(await _portfolios.GetHoldingsAsync(id, ct));

    // GET /api/portfolios/{id}/summary
    [HttpGet("{id:int}/summary")]
    public async Task<ActionResult<PortfolioSummaryResponse>> Summary(int id, CancellationToken ct) =>
        Ok(await _portfolios.GetSummaryAsync(id, ct));

    // GET /api/portfolios/{id}/transactions?symbol=&type=&from=&to=&page=&pageSize=
    // (POST on the same route lives in TransactionsController; the HTTP verbs differ, so there is no clash.)
    [HttpGet("{id:int}/transactions")]
    public async Task<ActionResult<PagedResponse<TransactionListItem>>> Transactions(
        int id,
        [FromQuery] string? symbol,
        [FromQuery] string? type,
        [FromQuery(Name = "from")] string? fromDate,
        [FromQuery(Name = "to")] string? toDate,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new TransactionQuery(symbol, type, fromDate, toDate, page, pageSize);
        return Ok(await _portfolios.ListTransactionsAsync(id, query, ct));
    }
}