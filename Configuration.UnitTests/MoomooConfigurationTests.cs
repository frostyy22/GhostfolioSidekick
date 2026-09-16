using AwesomeAssertions;

namespace GhostfolioSidekick.Configuration.UnitTests
{
	public class MoomooConfigurationTests
	{
		[Fact]
		public void Parse_MoomooConfiguration_ParsedCorrectly()
		{
			const string json = """
			{
				"moomoo": [
					{
						"name": "Synthetic Connection",
						"host": "opend.example.invalid",
						"port": 0,
						"enabled": true,
						"history-days": 30,
						"market-accounts": {
							"US": "Synthetic US Account",
							"MY": "Synthetic MY Account"
						},
						"cash-and-funds-account": "Synthetic Cash and Funds"
					}
				]
			}
			""";

			var configuration = ConfigurationInstance.Parse(json);

			configuration.Should().NotBeNull();
			configuration!.Moomoo.Should().ContainSingle();
			var moomoo = configuration.Moomoo![0];
			moomoo.Name.Should().Be("Synthetic Connection");
			moomoo.Host.Should().Be("opend.example.invalid");
			moomoo.Port.Should().Be(0);
			moomoo.HistoryDays.Should().Be(30);
			moomoo.MarketAccounts["US"].Should().Be("Synthetic US Account");
			moomoo.MarketAccounts["MY"].Should().Be("Synthetic MY Account");
			moomoo.CashAndFundsAccount.Should().Be("Synthetic Cash and Funds");
		}
	}
}
