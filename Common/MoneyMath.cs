namespace PortfolioApi.Common;

public static class MoneyRounding
{
    // Money and percentages: 2 decimal places, away from zero.
    public static decimal ToMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Average cost is stored to 4 decimal places.
    public static decimal ToAverageCost(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    public static decimal ToPrice(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static decimal ToPrice(decimal? realizedPnl)
    {
        throw new NotImplementedException();
    }
}