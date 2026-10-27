namespace PortfolioApi.Data.Entities
{
    public class Transaction
    {
        public int Id { get; set; }
        public int PortfolioId { get; set; }
        public int AssetId { get; set; }
        public Enums.TransactionType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Fee { get; set; }
        public DateTime ExecutedAtUtc { get; set; }

        public Portfolio Portfolio { get; set; } = null!;
        public Asset Asset { get; set; } = null!;
    }
}
