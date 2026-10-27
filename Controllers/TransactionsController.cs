using Microsoft.AspNetCore.Mvc;

using PortfolioApi.DTOs;
using PortfolioApi.Services;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:int}/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactions;
    public TransactionsController(ITransactionService transactions) => _transactions = transactions;

    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Create(
        int portfolioId, CreateTransactionRequest request, CancellationToken ct)
    {
        var result = await _transactions.CreateTransactionAsync(portfolioId, request, ct);
        return Created($"/api/portfolios/{portfolioId}/transactions/{result.Id}", result);
    }
}