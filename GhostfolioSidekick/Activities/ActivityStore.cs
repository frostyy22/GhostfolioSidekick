using GhostfolioSidekick.Database;
using GhostfolioSidekick.Model.Activities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.EntityFrameworkCore;

namespace GhostfolioSidekick.Activities
{
	public class ActivityStore(IDbContextFactory<DatabaseContext> databaseContextFactory) : IActivityStore
	{
		public async Task Sync(
			IEnumerable<Activity> activities,
			IEnumerable<string> accountNames,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(activities);
			ArgumentNullException.ThrowIfNull(accountNames);

			List<Activity> materializedActivities = [.. activities];
			HashSet<string> scope = accountNames
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.ToHashSet(StringComparer.Ordinal);

			if (scope.Count == 0)
			{
				if (materializedActivities.Count == 0)
				{
					return;
				}

				throw new ArgumentException("At least one account name is required when activities are supplied.", nameof(accountNames));
			}

			if (materializedActivities.Any(x => !scope.Contains(x.Account.Name)))
			{
				throw new InvalidOperationException("All activities must belong to an account included in the synchronization scope.");
			}

			using var databaseContext = await databaseContextFactory.CreateDbContextAsync(cancellationToken);

			Dictionary<string, Model.Accounts.Account> trackedAccounts = await databaseContext.Accounts
				.Where(x => scope.Contains(x.Name))
				.ToDictionaryAsync(x => x.Name, StringComparer.Ordinal, cancellationToken);

			List<string> missingAccounts = scope.Except(trackedAccounts.Keys, StringComparer.Ordinal).ToList();
			if (missingAccounts.Count > 0)
			{
				throw new InvalidOperationException($"The following accounts do not exist: {string.Join(", ", missingAccounts)}");
			}

			foreach (Activity activity in materializedActivities)
			{
				activity.Account = trackedAccounts[activity.Account.Name];
			}

			await UpdatePartialSymbolIdentifiers(databaseContext, materializedActivities, cancellationToken);

			List<Activity> existingActivities = await databaseContext.Activities
				.Include(x => x.Account)
				.Where(x => scope.Contains(x.Account.Name))
				.ToListAsync(cancellationToken);

			List<(string TransactionId, int AccountId)> existingKeys = GetTransactionKeys(existingActivities);
			List<(string TransactionId, int AccountId)> newKeys = GetTransactionKeys(materializedActivities);

			DeleteRemovedActivities(databaseContext, existingActivities, existingKeys, newKeys);
			await AddNewActivities(databaseContext, materializedActivities, existingKeys, newKeys, cancellationToken);
			await UpdateExistingActivities(databaseContext, existingActivities, materializedActivities, existingKeys, newKeys, cancellationToken);

			await databaseContext.SaveChangesAsync(cancellationToken);
		}

		private static async Task UpdatePartialSymbolIdentifiers(
			DatabaseContext databaseContext,
			IEnumerable<Activity> activities,
			CancellationToken cancellationToken)
		{
			var existingPartialSymbolIdentifiers = await databaseContext.PartialSymbolIdentifiers.ToListAsync(cancellationToken);

			foreach (var activity in activities.OfType<IActivityWithPartialIdentifier>())
			{
				var newPartialSymbolIdentifiers = activity.PartialSymbolIdentifiers
					.Where(partialSymbolIdentifier => !existingPartialSymbolIdentifiers.Any(x => x == partialSymbolIdentifier))
					.ToList();

				if (newPartialSymbolIdentifiers.Count > 0)
				{
					await databaseContext.PartialSymbolIdentifiers.AddRangeAsync(newPartialSymbolIdentifiers, cancellationToken);
					existingPartialSymbolIdentifiers.AddRange(newPartialSymbolIdentifiers);
				}

				activity.PartialSymbolIdentifiers =
				[
					.. activity.PartialSymbolIdentifiers.Select(
						x => existingPartialSymbolIdentifiers.FirstOrDefault(y => y == x) ?? x)
				];
			}
		}

		private static List<(string TransactionId, int AccountId)> GetTransactionKeys(IEnumerable<Activity> activities)
		{
			return
			[
				.. activities
					.Select(x => (x.TransactionId, x.Account.Id))
					.Distinct()
			];
		}

		private static void DeleteRemovedActivities(
			DatabaseContext databaseContext,
			List<Activity> existingActivities,
			List<(string TransactionId, int AccountId)> existingKeys,
			List<(string TransactionId, int AccountId)> newKeys)
		{
			foreach (var (transactionId, accountId) in existingKeys.Except(newKeys))
			{
				var activitiesToDelete = existingActivities.Where(x =>
					x.TransactionId == transactionId &&
					x.Account.Id == accountId);

				databaseContext.Activities.RemoveRange(activitiesToDelete);
			}
		}

		private static async Task AddNewActivities(
			DatabaseContext databaseContext,
			IEnumerable<Activity> activities,
			List<(string TransactionId, int AccountId)> existingKeys,
			List<(string TransactionId, int AccountId)> newKeys,
			CancellationToken cancellationToken)
		{
			foreach (var (transactionId, accountId) in newKeys.Except(existingKeys))
			{
				var activitiesToAdd = activities.Where(x =>
					x.TransactionId == transactionId &&
					x.Account.Id == accountId);

				await databaseContext.Activities.AddRangeAsync(activitiesToAdd, cancellationToken);
			}
		}

		private static async Task UpdateExistingActivities(
			DatabaseContext databaseContext,
			List<Activity> existingActivities,
			IEnumerable<Activity> activities,
			List<(string TransactionId, int AccountId)> existingKeys,
			List<(string TransactionId, int AccountId)> newKeys,
			CancellationToken cancellationToken)
		{
			foreach (var (transactionId, accountId) in existingKeys.Intersect(newKeys))
			{
				var existingActivity = existingActivities
					.Where(x => x.TransactionId == transactionId && x.Account.Id == accountId)
					.OrderBy(x => x.SortingPriority)
					.ThenBy(x => x.Description);

				var newActivity = activities
					.Where(x => x.TransactionId == transactionId && x.Account.Id == accountId)
					.OrderBy(x => x.SortingPriority)
					.ThenBy(x => x.Description);

				if (!AreActivitiesEqual(existingActivity, newActivity))
				{
					databaseContext.Activities.RemoveRange(existingActivity);
					await databaseContext.Activities.AddRangeAsync(newActivity, cancellationToken);
				}
			}
		}

		private static bool AreActivitiesEqual(IEnumerable<Activity> existing, IEnumerable<Activity> newActivities)
		{
			var compareLogic = new CompareLogic()
			{
				Config = new ComparisonConfig
				{
					MaxDifferences = int.MaxValue,
					IgnoreObjectTypes = true,
					MembersToIgnore =
					[
						nameof(Activity.Id),
						nameof(ActivityWithQuantityAndUnitPrice.AdjustedQuantity),
						nameof(ActivityWithQuantityAndUnitPrice.AdjustedUnitPrice),
						nameof(ActivityWithQuantityAndUnitPrice.AdjustedUnitPriceSource),
					]
				}
			};

			return compareLogic.Compare(existing, newActivities).AreEqual;
		}
	}
}
