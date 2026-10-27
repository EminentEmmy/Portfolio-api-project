namespace PortfolioApi.Common;

public readonly record struct PositionState(decimal Quantity, decimal AverageCost, decimal RealizedPnl)
{
    public static PositionState Empty => new(0m, 0m, 0m);
}

public readonly record struct TradeResult(
    PositionState Position,
    decimal GrossAmount,
    decimal NetAmount,
    decimal? RealizedPnl);          // null for a Buy

public readonly record struct PositionValuation(
    decimal CostBasis,
    decimal MarketValue,
    decimal UnrealizedPnl,
    decimal UnrealizedPnlPercent);  // raw values; round when building the DTO

public static class PositionCalculator
{
    public static TradeResult Buy(PositionState current, decimal quantity, decimal unitPrice, decimal fee)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");

        var gross = quantity * unitPrice;
        var newQuantity = current.Quantity + quantity;

        // The fee is part of the cost of the shares.
        var newAverage = (current.Quantity * current.AverageCost + gross + fee) / newQuantity;

        var position = current with
        {
            Quantity = newQuantity,
            AverageCost = MoneyRounding.ToAverageCost(newAverage)   // stored to 4 dp
        };

        return new TradeResult(position, gross, gross + fee, null);
    }

    public static TradeResult Sell(PositionState current, decimal quantity, decimal unitPrice, decimal fee)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");
        if (quantity > current.Quantity)
            throw new InvalidOperationException($"Cannot sell {quantity}; position holds {current.Quantity}.");

        var gross = quantity * unitPrice;
        var cashIn = gross - fee;
        var tradePnl = cashIn - quantity * current.AverageCost;     // raw, not rounded yet

        var remaining = current.Quantity - quantity;
        var position = new PositionState(
            Quantity: remaining,
            AverageCost: remaining == 0 ? 0m : current.AverageCost,   // a sell never changes it; reset at zero
            RealizedPnl: MoneyRounding.ToMoney(current.RealizedPnl + tradePnl));

        return new TradeResult(position, gross, cashIn, MoneyRounding.ToMoney(tradePnl));
    }

    public static PositionValuation Value(decimal quantity, decimal averageCost, decimal currentPrice)
    {
        var costBasis = quantity * averageCost;
        var marketValue = quantity * currentPrice;
        var unrealized = marketValue - costBasis;
        var percent = costBasis == 0 ? 0m : unrealized / costBasis * 100m;
        return new PositionValuation(costBasis, marketValue, unrealized, percent);
    }
}