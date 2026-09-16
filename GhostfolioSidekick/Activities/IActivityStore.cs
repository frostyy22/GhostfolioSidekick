using GhostfolioSidekick.Model.Activities;

namespace GhostfolioSidekick.Activities
{
	public interface IActivityStore
	{
		Task Sync(IEnumerable<Activity> activities, IEnumerable<string> accountNames, CancellationToken cancellationToken = default);
	}
}
