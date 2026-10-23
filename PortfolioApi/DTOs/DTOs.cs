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
        public int AssetId { get; set; }
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
        public int AssetId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Fee { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal NetAmount { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal RealizedPnl { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
    }
}
