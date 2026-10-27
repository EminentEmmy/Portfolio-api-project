using static PortfolioApi.Data.Entities.Enums;

namespace PortfolioApi.DTOs
{

    public record CreateAssetRequest(string? Symbol, string? Name, string? AssetType, decimal? CurrentPrice);
    public record UpdatePriceRequest(decimal? Price);
    public record AssetResponse(
        int Id, string Symbol, string Name, AssetType AssetType,
        decimal CurrentPrice, DateTime PriceUpdatedAtUtc);

    public record CreatePortfolioRequest(string? Name, string? OwnerEmail, string? BaseCurrency);
    public record PortfolioResponse(
        int Id, string Name, string OwnerEmail, string BaseCurrency, DateTime CreatedAtUtc);


    public class CreateTransactionRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Fee { get; set; }
        public string Type { get; set; } = string.Empty;
    }

    public class TransactionResponse
    {
        public int Id { get; set; }
        public int PortfolioId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Fee { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal? RealizedPnl { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
    }

    // ---------------------------------------------------------------
    // GET /api/portfolios/{id}/holdings
    // One row per open position (quantity above 0), highest market value first.
    // ---------------------------------------------------------------
    public record HoldingResponse(
        string Symbol,
        decimal Quantity,
        decimal AverageCost,
        decimal CostBasis,
        decimal CurrentPrice,
        decimal MarketValue,
        decimal UnrealizedPnl,
        decimal UnrealizedPnlPercent,
        decimal RealizedPnl);

    // ---------------------------------------------------------------
    // GET /api/portfolios/{id}/summary
    // ---------------------------------------------------------------
    public record AllocationResponse(AssetType AssetType, decimal MarketValue, decimal Percent);

    public record PortfolioSummaryResponse(
        int PortfolioId,
        string Name,
        string BaseCurrency,
        int HoldingsCount,
        decimal TotalMarketValue,
        decimal TotalCostBasis,
        decimal UnrealizedPnl,
        decimal UnrealizedPnlPercent,
        decimal RealizedPnl,
        decimal TotalReturn,
        List<AllocationResponse> Allocation);

    // ---------------------------------------------------------------
    // GET /api/portfolios/{id}/transactions   (paged, newest first)
    // ---------------------------------------------------------------
    public record TransactionQuery(
        string? Symbol,
        string? Type,
        string? From,
        string? To,
        int Page,
        int PageSize);

    public record TransactionListItem(
        int Id,
        int PortfolioId,
        string Symbol,
        TransactionType Type,
        decimal Quantity,
        decimal UnitPrice,
        decimal Fee,
        decimal GrossAmount,
        decimal NetAmount,
        DateTime ExecutedAtUtc);

    public record PagedResponse<T>(List<T> Items, int Page, int PageSize, int TotalCount);
}