namespace PortfolioApi.Data.Entities
{
    public class Asset
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public Enums.AssetType AssetType { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTime PriceUpdatedUtc { get; set; }
        public DateTime PriceUpdatedAtUtc { get; internal set; }
    }
}
