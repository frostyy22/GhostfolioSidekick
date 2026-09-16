using AwesomeAssertions;
using GhostfolioSidekick.Configuration;
using GhostfolioSidekick.Model.Activities;
using GhostfolioSidekick.Parsers.Moomoo;

namespace GhostfolioSidekick.Parsers.UnitTests.Moomoo
{
	public class MoomooNormalizerTests
	{
		private readonly MoomooNormalizer normalizer = new();

		[Fact]
		public void Normalize_BuyWithFee_RoutesToConfiguredMarketAccount()
		{
			var configuration = CreateConfiguration();
			var trade = new MoomooTrade(
				"synthetic-order-001",
				"US.TEST",
				MoomooTradeSide.Buy,
				2,
				10.50m,
				"USD",
				new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
				0.25m,
				MoomooAssetType.Etf,
				"Synthetic ETF");

			var result = normalizer.Normalize(configuration, [trade], []);

			result.Should().ContainKey("Synthetic US Account");
			var activities = result["Synthetic US Account"].ToList();
			activities.Should().HaveCount(2);
			activities.Should().ContainSingle(x => x.ActivityType == PartialActivityType.Buy);
			activities.Should().ContainSingle(x => x.ActivityType == PartialActivityType.Fee);
			activities.Should().OnlyContain(x => x.TransactionId == "MOOMOO_synthetic-order-001");

			var buy = activities.Single(x => x.ActivityType == PartialActivityType.Buy);
			buy.SymbolIdentifiers.Should().Contain(x => x?.Identifier == "TEST");
			buy.SymbolIdentifiers.Should().Contain(x => x?.Identifier == "Synthetic ETF");
			buy.SymbolIdentifiers
				.Where(x => x != null)
				.Should()
				.OnlyContain(x => x!.AllowedAssetSubClasses.Contains(AssetSubClass.Etf));
		}

		[Fact]
		public void Normalize_CashBalance_RoutesToConfiguredCashAndFundsAccount()
		{
			var configuration = CreateConfiguration();
			var balance = new MoomooCashBalance(
				"MYR",
				123.45m,
				new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

			var result = normalizer.Normalize(configuration, [], [balance]);

			result.Should().ContainKey("Synthetic Cash and Funds");
			var activity = result["Synthetic Cash and Funds"].Should().ContainSingle().Subject;
			activity.ActivityType.Should().Be(PartialActivityType.KnownBalance);
			activity.Amount.Should().Be(123.45m);
			activity.TransactionId.Should().Be("MOOMOO_BALANCE_MYR_20260102030405");
		}

		[Theory]
		[InlineData("US.TEST", "TEST")]
		[InlineData("HK.00700", "0700.HK")]
		[InlineData("MY.1234", "1234.KL")]
		[InlineData("SG.TEST", "TEST.SI")]
		[InlineData("JP.1234", "1234.T")]
		[InlineData("AU.TEST", "TEST.AX")]
		[InlineData("CA.TEST", "TEST.TO")]
		[InlineData("SH.600000", "600000.SS")]
		[InlineData("SZ.000001", "000001.SZ")]
		public void NormalizeTicker_KnownMarket_ReturnsProviderFriendlyTicker(string source, string expected)
		{
			MoomooNormalizer.NormalizeTicker(source).Should().Be(expected);
		}

		[Fact]
		public void Normalize_UnconfiguredMarket_ThrowsClearError()
		{
			var configuration = CreateConfiguration();
			var trade = new MoomooTrade(
				"synthetic-order-002",
				"HK.00700",
				MoomooTradeSide.Sell,
				1,
				100m,
				"HKD",
				DateTime.UtcNow);

			Action action = () => normalizer.Normalize(configuration, [trade], []);

			action.Should()
				.Throw<InvalidOperationException>()
				.WithMessage("*HK*");
		}

		private static MoomooConfiguration CreateConfiguration()
		{
			return new MoomooConfiguration
			{
				Name = "Synthetic Moomoo Connection",
				Host = "opend.example.invalid",
				Port = 0,
				MarketAccounts = new Dictionary<string, string>
				{
					["US"] = "Synthetic US Account"
				},
				CashAndFundsAccount = "Synthetic Cash and Funds"
			};
		}
	}
}
