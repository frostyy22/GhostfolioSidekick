using System.Text.Json.Serialization;

namespace GhostfolioSidekick.Configuration
{
	public class MoomooConfiguration
	{
		[JsonPropertyName("name")]
		public required string Name { get; set; }

		[JsonPropertyName("host")]
		public required string Host { get; set; }

		[JsonPropertyName("port")]
		public required int Port { get; set; }

		[JsonPropertyName("enabled")]
		public bool Enabled { get; set; } = true;

		[JsonPropertyName("history-days")]
		public int HistoryDays { get; set; } = 95;

		[JsonPropertyName("market-accounts")]
		public Dictionary<string, string> MarketAccounts { get; set; } = [];

		[JsonPropertyName("cash-and-funds-account")]
		public string? CashAndFundsAccount { get; set; }
	}
}
