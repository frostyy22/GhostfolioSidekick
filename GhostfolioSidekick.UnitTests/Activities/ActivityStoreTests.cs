using AwesomeAssertions;
using GhostfolioSidekick.Activities;
using GhostfolioSidekick.Model.Accounts;
using GhostfolioSidekick.Model.Activities;
using GhostfolioSidekick.Model.Activities.Types;
using Microsoft.EntityFrameworkCore;

namespace GhostfolioSidekick.UnitTests.Activities
{
	public class ActivityStoreTests : IDisposable
	{
		private readonly CustomDbContextFactory databaseContextFactory = new();

		public void Dispose()
		{
			databaseContextFactory.Dispose();
		}

		[Fact]
		public async Task Sync_OneAccount_DoesNotDeleteOtherAccountActivities()
		{
			await SeedTwoAccounts();
			var store = new ActivityStore(databaseContextFactory);

			var replacement = new BuyActivity
			{
				TransactionId = "SCOPED_NEW",
				Account = new Account { Name = "Scoped Account" }
			};

			await store.Sync([replacement], ["Scoped Account"], TestContext.Current.CancellationToken);

			using var context = databaseContextFactory.CreateDbContext();
			var activities = await context.Activities
				.Include(x => x.Account)
				.ToListAsync(TestContext.Current.CancellationToken);

			activities.Should().ContainSingle(x =>
				x.Account.Name == "Scoped Account" &&
				x.TransactionId == "SCOPED_NEW");
			activities.Should().NotContain(x =>
				x.Account.Name == "Scoped Account" &&
				x.TransactionId == "SCOPED_OLD");
			activities.Should().ContainSingle(x =>
				x.Account.Name == "Unrelated Account" &&
				x.TransactionId == "UNRELATED");
		}

		[Fact]
		public async Task Sync_EmptyScopedAccount_OnlyClearsThatAccount()
		{
			await SeedTwoAccounts();
			var store = new ActivityStore(databaseContextFactory);

			await store.Sync([], ["Scoped Account"], TestContext.Current.CancellationToken);

			using var context = databaseContextFactory.CreateDbContext();
			var activities = await context.Activities
				.Include(x => x.Account)
				.ToListAsync(TestContext.Current.CancellationToken);

			activities.Should().NotContain(x => x.Account.Name == "Scoped Account");
			activities.Should().ContainSingle(x =>
				x.Account.Name == "Unrelated Account" &&
				x.TransactionId == "UNRELATED");
		}

		private async Task SeedTwoAccounts()
		{
			using var context = databaseContextFactory.CreateDbContext();
			var scopedAccount = new Account { Name = "Scoped Account" };
			var unrelatedAccount = new Account { Name = "Unrelated Account" };
			context.Accounts.AddRange(scopedAccount, unrelatedAccount);
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);

			context.Activities.AddRange(
				new BuyActivity
				{
					TransactionId = "SCOPED_OLD",
					Account = scopedAccount
				},
				new BuyActivity
				{
					TransactionId = "UNRELATED",
					Account = unrelatedAccount
				});
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);
		}
	}
}
