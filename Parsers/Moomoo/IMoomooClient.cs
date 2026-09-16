using GhostfolioSidekick.Configuration;

namespace GhostfolioSidekick.Parsers.Moomoo
{
	public interface IMoomooClient : IAsyncDisposable
	{
		Task<MoomooSnapshot> GetSnapshot(
			MoomooConfiguration configuration,
			DateTime from,
			CancellationToken cancellationToken = default);
	}

	public sealed record MoomooSnapshot(
		IReadOnlyCollection<MoomooTrade> Trades,
		IReadOnlyCollection<MoomooCashBalance> CashBalances);
}
