using PortfolioApi.Common;
using Xunit;

namespace PortfolioApi.Tests;

public class SummaryAndRealizedTests
{
    [Fact]
    public void Worked_example_summary_matches_step_12()
    {
        // Steps 5-7: ACME
        var acme = PositionCalculator.Buy(PositionState.Empty, 100m, 50m, 10m).Position;
        acme = PositionCalculator.Buy(acme, 100m, 54m, 10m).Position;
        acme = PositionCalculator.Sell(acme, 50m, 60m, 10m).Position;

        // Steps 8-9: GOVB and INDX
        var govb = PositionCalculator.Buy(PositionState.Empty, 10m, 980m, 0m).Position;
        var indx = PositionCalculator.Buy(PositionState.Empty, 40.5m, 100m, 0m).Position;

        // Step 10: new prices. Raw (unrounded) valuations feed the totals.
        var vGovb = PositionCalculator.Value(govb.Quantity, govb.AverageCost, 1000m);
        var vAcme = PositionCalculator.Value(acme.Quantity, acme.AverageCost, 58m);
        var vIndx = PositionCalculator.Value(indx.Quantity, indx.AverageCost, 110m);

        var totalMarketValue = vGovb.MarketValue + vAcme.MarketValue + vIndx.MarketValue;
        var totalCostBasis = vGovb.CostBasis + vAcme.CostBasis + vIndx.CostBasis;
        var unrealized = totalMarketValue - totalCostBasis;
        var realized = govb.RealizedPnl + acme.RealizedPnl + indx.RealizedPnl;

        Assert.Equal(23155.00m, MoneyRounding.ToMoney(totalMarketValue));
        Assert.Equal(21665.00m, MoneyRounding.ToMoney(totalCostBasis));
        Assert.Equal(1490.00m, MoneyRounding.ToMoney(unrealized));
        Assert.Equal(6.88m, MoneyRounding.ToMoney(unrealized / totalCostBasis * 100m));
        Assert.Equal(385.00m, MoneyRounding.ToMoney(realized));
        Assert.Equal(1875.00m, MoneyRounding.ToMoney(unrealized + realized));

        // Allocation: Bond, Stock, Fund
        Assert.Equal(43.19m, MoneyRounding.ToMoney(vGovb.MarketValue / totalMarketValue * 100m));
        Assert.Equal(37.57m, MoneyRounding.ToMoney(vAcme.MarketValue / totalMarketValue * 100m));
        Assert.Equal(19.24m, MoneyRounding.ToMoney(vIndx.MarketValue / totalMarketValue * 100m));
    }

    [Fact]
    public void Realized_pnl_accumulates_across_sells()
    {
        var position = new PositionState(200m, 52.10m, 0m);

        var firstSell = PositionCalculator.Sell(position, 50m, 60m, 10m);               // +385
        var secondSell = PositionCalculator.Sell(firstSell.Position, 50m, 50m, 0m);     // 2,500 - 2,605 = -105

        Assert.Equal(385m, firstSell.RealizedPnl);
        Assert.Equal(-105m, secondSell.RealizedPnl);          // this trade only
        Assert.Equal(280m, secondSell.Position.RealizedPnl);  // running total on the holding
    }

    [Fact]
    public void Selling_an_asset_never_held_is_rejected()
    {
        // Scenario 10: the service reports "holds 0" in the 409.
        Assert.Throws<InvalidOperationException>(
            () => PositionCalculator.Sell(PositionState.Empty, 1m, 60m, 0m));
    }

    [Fact]
    public void Buy_keeps_realized_pnl_from_earlier_sells()
    {
        var position = new PositionState(0m, 0m, 1570m);

        var trade = PositionCalculator.Buy(position, 10m, 70m, 0m);

        Assert.Equal(70.00m, trade.Position.AverageCost);
        Assert.Equal(1570m, trade.Position.RealizedPnl);
    }
}