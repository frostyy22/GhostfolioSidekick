using GhostfolioSidekick.Configuration;
using GhostfolioSidekick.Model;
using GhostfolioSidekick.Model.Activities;

namespace GhostfolioSidekick.Parsers.Moomoo
{
	public sealed class MoomooNormalizer
	{
		public IReadOnlyDictionary<string, IReadOnlyCollection<PartialActivity>> Normalize(
			MoomooConfiguration configuration,
			IEnumerable<MoomooTrade> trades,
			IEnumerable<MoomooCashBalance> cashBalances)
		{
			ArgumentNullException.ThrowIfNull(configuration);
			ArgumentNullException.ThrowIfNull(trades);
			ArgumentNullException.ThrowIfNull(cashBalances);

			Dictionary<string, List<PartialActivity>> activitiesByAccount = [];

			foreach (var trade in trades)
			{
				string market = GetMarket(trade.Code);
				if (!configuration.MarketAccounts.TryGetValue(market, out string? accountName) || string.IsNullOrWhiteSpace(accountName))
				{
					throw new InvalidOperationException($"No Ghostfolio account is configured for Moomoo market '{market}'.");
				}

				Currency currency = Currency.GetCurrency(trade.Currency);
				var identifiers = CreateIdentifiers(trade, currency);
				string transactionId = $"MOOMOO_{trade.OrderId}";
				Money unitPrice = new(currency, trade.UnitPrice);
				Money total = new(currency, trade.Quantity * trade.UnitPrice);

				var tradeActivity = trade.Side switch
				{
					MoomooTradeSide.Buy => PartialActivity.CreateBuy(currency, trade.Date, identifiers, trade.Quantity, unitPrice, total, transactionId),
					MoomooTradeSide.Sell => PartialActivity.CreateSell(currency, trade.Date, identifiers, trade.Quantity, unitPrice, total, transactionId),
					_ => throw new NotSupportedException($"Unsupported Moomoo trade side '{trade.Side}'.")
				};

				Add(activitiesByAccount, accountName, tradeActivity);

				if (trade.Fee > 0)
				{
					Add(
						activitiesByAccount,
						accountName,
						PartialActivity.CreateFee(currency, trade.Date, trade.Fee, new Money(currency, trade.Fee), transactionId));
				}
			}

			if (cashBalances.Any())
			{
				if (string.IsNullOrWhiteSpace(configuration.CashAndFundsAccount))
				{
					throw new InvalidOperationException("Moomoo cash balances were returned but no cash-and-funds-account is configured.");
				}

				foreach (var balance in cashBalances)
				{
					Currency currency = Currency.GetCurrency(balance.Currency);
					PartialActivity knownBalance = PartialActivity.CreateKnownBalance(currency, balance.Date, balance.Amount);
					knownBalance.TransactionId = $"MOOMOO_BALANCE_{currency.Symbol}_{balance.Date:yyyyMMddHHmmss}";
					Add(activitiesByAccount, configuration.CashAndFundsAccount, knownBalance);
				}
			}

			return activitiesByAccount.ToDictionary(
				x => x.Key,
				x => (IReadOnlyCollection<PartialActivity>)x.Value.AsReadOnly());
		}

		private static ICollection<PartialSymbolIdentifier?> CreateIdentifiers(MoomooTrade trade, Currency currency)
		{
			(List<AssetClass> classes, List<AssetSubClass> subClasses) = GetAssetClassification(trade.AssetType);
			List<PartialSymbolIdentifier?> result =
			[
				new PartialSymbolIdentifier(
					IdentifierType.Ticker,
					NormalizeTicker(trade.Code),
					currency,
					classes,
					subClasses)
			];

			if (!string.IsNullOrWhiteSpace(trade.Name))
			{
				result.Add(new PartialSymbolIdentifier(IdentifierType.Name, trade.Name, currency, classes, subClasses));
			}

			return result;
		}

		private static (List<AssetClass>, List<AssetSubClass>) GetAssetClassification(MoomooAssetType assetType)
		{
			return assetType switch
			{
				MoomooAssetType.Stock => ([AssetClass.Equity], [AssetSubClass.Stock]),
				MoomooAssetType.Etf => ([AssetClass.Equity], [AssetSubClass.Etf]),
				MoomooAssetType.MutualFund => ([AssetClass.Equity], [AssetSubClass.MutualFund]),
				MoomooAssetType.Bond => ([AssetClass.FixedIncome], [AssetSubClass.Bond]),
				MoomooAssetType.Commodity => ([AssetClass.Commodity], [AssetSubClass.Commodity]),
				MoomooAssetType.PreciousMetal => ([AssetClass.Commodity], [AssetSubClass.PreciousMetal]),
				MoomooAssetType.Future => ([AssetClass.Commodity], [AssetSubClass.Commodity]),
				MoomooAssetType.CryptoCurrency => ([AssetClass.Liquidity], [AssetSubClass.CryptoCurrency]),
				_ => ([], [])
			};
		}

		private static string GetMarket(string code)
		{
			int separator = code.IndexOf('.');
			if (separator <= 0)
			{
				throw new FormatException($"Moomoo security code '{code}' does not contain a market prefix.");
			}

			return code[..separator].ToUpperInvariant();
		}

		internal static string NormalizeTicker(string code)
		{
			int separator = code.IndexOf('.');
			if (separator <= 0 || separator == code.Length - 1)
			{
				throw new FormatException($"Moomoo security code '{code}' is not in MARKET.SYMBOL format.");
			}

			string market = code[..separator].ToUpperInvariant();
			string symbol = code[(separator + 1)..];

			return market switch
			{
				"US" => symbol,
				"HK" => $"{symbol.PadLeft(4, '0')}.HK",
				"MY" => $"{symbol}.KL",
				"SG" => $"{symbol}.SI",
				"JP" => $"{symbol}.T",
				"AU" => $"{symbol}.AX",
				"CA" => $"{symbol}.TO",
				"SH" => $"{symbol}.SS",
				"SZ" => $"{symbol}.SZ",
				_ => symbol
			};
		}

		private static void Add(Dictionary<string, List<PartialActivity>> activitiesByAccount, string accountName, PartialActivity activity)
		{
			if (!activitiesByAccount.TryGetValue(accountName, out List<PartialActivity>? activities))
			{
				activities = [];
				activitiesByAccount.Add(accountName, activities);
			}

			activities.Add(activity);
		}
	}
}
