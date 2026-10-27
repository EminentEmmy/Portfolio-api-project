namespace PortfolioApi.Data.Entities
{
    public class Holding
    {
        public int Id { get; set; }
        public int PortfolioId { get; set; }
        public int AssetId { get; set; }
        public decimal Quantity { get; set; }
        public decimal AverageCost { get; set; }
        public decimal RealizedPnl { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public Portfolio Portfolio { get; set; } = null!;
        public Asset Asset { get; set; } = null!;
    }
}
