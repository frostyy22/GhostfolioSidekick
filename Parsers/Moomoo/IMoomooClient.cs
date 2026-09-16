using GhostfolioSidekick.Configuration;

namespace GhostfolioSidekick.Parsers.Moomoo
{
	public interface IMoomooClient : IAsyncDisposable
	{
		Task<MoomooHealthResult> CheckHealth(
			MoomooConfiguration configuration,
			CancellationToken cancellationToken = default);
	}

	public sealed record MoomooHealthResult(
		bool Connected,
		bool RealAccountDetected,
		int RealAccountCount,
		string? Message = null);
}
