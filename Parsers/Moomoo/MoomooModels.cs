namespace GhostfolioSidekick.Parsers.Moomoo
{
	public enum MoomooTradeSide
	{
		Buy,
		Sell
	}

	public enum MoomooAssetType
	{
		Unknown,
		Stock,
		Etf,
		MutualFund,
		Bond,
		Commodity,
		PreciousMetal,
		Future,
		CryptoCurrency
	}

	public sealed record MoomooTrade(
		string OrderId,
		string Code,
		MoomooTradeSide Side,
		decimal Quantity,
		decimal UnitPrice,
		string Currency,
		DateTime Date,
		decimal Fee = 0,
		MoomooAssetType AssetType = MoomooAssetType.Unknown,
		string? Name = null);

	public sealed record MoomooCashBalance(
		string Currency,
		decimal Amount,
		DateTime Date);
}
