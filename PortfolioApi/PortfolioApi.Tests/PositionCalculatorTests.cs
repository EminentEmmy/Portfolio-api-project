using System;
using System.Collections.Generic;
using PortfolioApi;
using PortfolioApi.Common;
using Xunit;

namespace PortfolioApi.Tests;

public class PositionCalculatorTests
{
    [Fact]
    public void Acme_worked_example_steps_5_to_7()
    {
        var p = PositionState.Empty;

        p = PositionCalculator.Buy(p, 100m, 50m, 10m).Position;
        Assert.Equal(100m, p.Quantity);
        Assert.Equal(50.10m, p.AverageCost);

        p = PositionCalculator.Buy(p, 100m, 54m, 10m).Position;
        Assert.Equal(200m, p.Quantity);
        Assert.Equal(52.10m, p.AverageCost);

        var sell = PositionCalculator.Sell(p, 50m, 60m, 10m);
        Assert.Equal(3000m, sell.GrossAmount);
        Assert.Equal(2990m, sell.NetAmount);
        Assert.Equal(385.00m, sell.RealizedPnl);
        Assert.Equal(150m, sell.Position.Quantity);
        Assert.Equal(52.10m, sell.Position.AverageCost);   // unchanged by a sell
        Assert.Equal(385.00m, sell.Position.RealizedPnl);
    }

    [Fact]
    public void Buy_returns_net_amount_with_fee_and_no_realized_pnl()
    {
        var buy = PositionCalculator.Buy(PositionState.Empty, 100m, 50m, 10m);
        Assert.Equal(5000m, buy.GrossAmount);
        Assert.Equal(5010m, buy.NetAmount);
        Assert.Null(buy.RealizedPnl);
    }

    [Fact]
    public void Selling_everything_resets_average_cost_then_new_buy_starts_fresh()   // scenario 13
    {
        var held = new PositionState(150m, 52.10m, 385.00m);

        var soldOut = PositionCalculator.Sell(held, 150m, 58m, 0m).Position;
        Assert.Equal(0m, soldOut.Quantity);
        Assert.Equal(0m, soldOut.AverageCost);
        Assert.Equal(385.00m + 885.00m, soldOut.RealizedPnl);   // 150 x (58 - 52.10) = 885

        var rebought = PositionCalculator.Buy(soldOut, 10m, 70m, 0m).Position;
        Assert.Equal(70.00m, rebought.AverageCost);
    }

    [Fact]
    public void Oversell_is_rejected()
    {
        var held = new PositionState(150m, 52.10m, 0m);
        Assert.Throws<InvalidOperationException>(() => PositionCalculator.Sell(held, 500m, 60m, 0m));
    }

    [Fact]
    public void Zero_quantity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PositionCalculator.Buy(PositionState.Empty, 0m, 50m, 0m));
    }

    [Fact]
    public void Holdings_values_after_price_change_steps_10_and_11()
    {
        var acme = PositionCalculator.Value(150m, 52.10m, 58m);
        Assert.Equal(7815.00m, MoneyRounding.ToMoney(acme.CostBasis));
        Assert.Equal(8700.00m, MoneyRounding.ToMoney(acme.MarketValue));
        Assert.Equal(885.00m, MoneyRounding.ToMoney(acme.UnrealizedPnl));
        Assert.Equal(11.32m, MoneyRounding.ToMoney(acme.UnrealizedPercent));

        var govb = PositionCalculator.Value(10m, 980m, 1000m);
        Assert.Equal(2.04m, MoneyRounding.ToMoney(govb.UnrealizedPercent));

        var indx = PositionCalculator.Value(40.5m, 100m, 110m);
        Assert.Equal(4455.00m, MoneyRounding.ToMoney(indx.MarketValue));
        Assert.Equal(10.00m, MoneyRounding.ToMoney(indx.UnrealizedPercent));
    }

    [Fact]
    public void Percent_is_zero_when_cost_basis_is_zero()
    {
        Assert.Equal(0m, PositionCalculator.Value(0m, 0m, 100m).UnrealizedPercent);
    }

    [Fact]
    public void Rounding_uses_away_from_zero_not_bankers_rounding()
    {
        Assert.Equal(2.35m, MoneyRounding.ToMoney(2.345m));    // plain Math.Round(2.345m, 2) gives 2.34
        Assert.Equal(-2.35m, MoneyRounding.ToMoney(-2.345m));
        Assert.Equal(0.13m, MoneyRounding.ToMoney(0.125m));
    }
}
