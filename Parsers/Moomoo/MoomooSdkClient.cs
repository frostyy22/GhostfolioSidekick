using GhostfolioSidekick.Configuration;

namespace GhostfolioSidekick.Parsers.Moomoo
{
	public sealed class MoomooSdkClient : IMoomooClient, MMSPI_Trd, MMSPI_Conn
	{
		private static readonly object ApiInitializationLock = new();
		private static int apiUserCount;

		private readonly object stateLock = new();
		private MMAPI_Trd? tradeApi;
		private TaskCompletionSource<MoomooHealthResult>? healthCompletionSource;
		private uint accountListSequence;
		private bool disposed;

		public MoomooSdkClient()
		{
			AcquireApi();
		}

		public async Task<MoomooHealthResult> CheckHealth(
			MoomooConfiguration configuration,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(configuration);
			ObjectDisposedException.ThrowIf(disposed, this);

			if (string.IsNullOrWhiteSpace(configuration.Host))
			{
				throw new ArgumentException("Moomoo OpenD host must be configured.", nameof(configuration));
			}

			if (configuration.Port is < 1 or > ushort.MaxValue)
			{
				throw new ArgumentOutOfRangeException(nameof(configuration), "Moomoo OpenD port must be between 1 and 65535.");
			}

			TaskCompletionSource<MoomooHealthResult> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

			lock (stateLock)
			{
				if (healthCompletionSource != null)
				{
					throw new InvalidOperationException("A Moomoo health check is already in progress for this client.");
				}

				healthCompletionSource = completionSource;
				accountListSequence = 0;
				tradeApi = new MMAPI_Trd();
				tradeApi.SetClientInfo("GhostfolioSidekick", 1);
				tradeApi.SetConnCallback(this);
				tradeApi.SetTrdCallback(this);
			}

			using CancellationTokenRegistration registration = cancellationToken.Register(() =>
				completionSource.TrySetCanceled(cancellationToken));

			try
			{
				bool started = tradeApi.InitConnect(configuration.Host, checked((ushort)configuration.Port), false);
				if (!started)
				{
					return new MoomooHealthResult(false, false, 0, "OpenD connection attempt could not be started.");
				}

				return await completionSource.Task.ConfigureAwait(false);
			}
			finally
			{
				CloseConnection();
				lock (stateLock)
				{
					healthCompletionSource = null;
					accountListSequence = 0;
				}
			}
		}

		public void OnInitConnect(MMAPI_Conn client, long errCode, string desc)
		{
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			MMAPI_Trd? api;

			lock (stateLock)
			{
				completionSource = healthCompletionSource;
				api = tradeApi;
			}

			if (completionSource == null || api == null)
			{
				return;
			}

			if (errCode != 0)
			{
				completionSource.TrySetResult(new MoomooHealthResult(false, false, 0, BuildConnectionError(errCode, desc)));
				return;
			}

			TrdGetAccList.C2S c2s = TrdGetAccList.C2S.CreateBuilder()
				.SetUserID(0)
				.SetTrdCategory((int)TrdCommon.TrdCategory.TrdCategory_Security)
				.SetNeedGeneralSecAccount(true)
				.Build();

			TrdGetAccList.Request request = TrdGetAccList.Request.CreateBuilder()
				.SetC2S(c2s)
				.Build();

			uint sequence = api.GetAccList(request);
			lock (stateLock)
			{
				accountListSequence = sequence;
			}
		}

		public void OnDisconnect(MMAPI_Conn client, long errCode)
		{
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			lock (stateLock)
			{
				completionSource = healthCompletionSource;
			}

			completionSource?.TrySetResult(
				new MoomooHealthResult(false, false, 0, $"OpenD disconnected before the health check completed (code {errCode})."));
		}

		public void OnReply_GetAccList(MMAPI_Conn client, uint serialNo, TrdGetAccList.Response response)
		{
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			uint expectedSequence;

			lock (stateLock)
			{
				completionSource = healthCompletionSource;
				expectedSequence = accountListSequence;
			}

			if (completionSource == null || serialNo != expectedSequence)
			{
				return;
			}

			if (response.RetType != 0 || !response.HasS2C)
			{
				string message = string.IsNullOrWhiteSpace(response.RetMsg)
					? $"OpenD account discovery failed with return type {response.RetType}."
					: $"OpenD account discovery failed: {response.RetMsg}";
				completionSource.TrySetResult(new MoomooHealthResult(true, false, 0, message));
				return;
			}

			int realAccountCount = response.S2C.AccListList.Count(account =>
				account.TrdEnv == (int)TrdCommon.TrdEnv.TrdEnv_Real);

			completionSource.TrySetResult(
				new MoomooHealthResult(
					true,
					realAccountCount > 0,
					realAccountCount,
					realAccountCount > 0
						? "OpenD is reachable and at least one REAL securities account is available."
						: "OpenD is reachable, but no REAL securities account was returned."));
		}

		public ValueTask DisposeAsync()
		{
			if (disposed)
			{
				return ValueTask.CompletedTask;
			}

			disposed = true;
			CloseConnection();
			ReleaseApi();
			GC.SuppressFinalize(this);
			return ValueTask.CompletedTask;
		}

		private void CloseConnection()
		{
			MMAPI_Trd? api;
			lock (stateLock)
			{
				api = tradeApi;
				tradeApi = null;
			}

			api?.Close();
		}

		private static string BuildConnectionError(long errCode, string description)
		{
			return string.IsNullOrWhiteSpace(description)
				? $"Unable to connect to OpenD (code {errCode})."
				: $"Unable to connect to OpenD (code {errCode}): {description}";
		}

		private static void AcquireApi()
		{
			lock (ApiInitializationLock)
			{
				if (apiUserCount == 0)
				{
					MMAPI.Init();
				}
				apiUserCount++;
			}
		}

		private static void ReleaseApi()
		{
			lock (ApiInitializationLock)
			{
				apiUserCount--;
				if (apiUserCount == 0)
				{
					MMAPI.UnInit();
				}
			}
		}
	}
}
