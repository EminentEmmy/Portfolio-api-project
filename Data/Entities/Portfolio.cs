namespace PortfolioApi.Data.Entities
{
    public class Portfolio
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string BaseCurrency { get; set; } = "USD";
        public DateTime CreatedAtUtc { get; set; }

        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
        public List<Holding> Holdings { get; set; } = new List<Holding>();
    }
}
