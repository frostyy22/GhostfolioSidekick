using GhostfolioSidekick.Configuration;
using Moomoo.OpenApi;
using Moomoo.OpenApi.Pb;
using System.Reflection;

namespace GhostfolioSidekick.Parsers.Moomoo
{
	public sealed class MoomooSdkClient : IMoomooClient
	{
		private static readonly object ApiInitializationLock = new();
		private static bool apiInitialized;
		private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(10);

		private readonly object stateLock = new();
		private MMAPI_Trd? tradeApi;
		private TaskCompletionSource<MoomooHealthResult>? healthCompletionSource;
		private uint accountListSequence;
		private bool disposed;

		public MoomooSdkClient()
		{
			EnsureApiInitialized();
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
			MMAPI_Trd api;

			lock (stateLock)
			{
				if (healthCompletionSource != null)
				{
					throw new InvalidOperationException("A Moomoo health check is already in progress for this client.");
				}

				healthCompletionSource = completionSource;
				accountListSequence = 0;
				api = new MMAPI_Trd();
				api.SetClientInfo("GhostfolioSidekick", 1);
				api.SetConnCallback(CreateProxy<MMSPI_Conn>(ProcessConnectionCallback));
				api.SetTrdCallback(CreateProxy<MMSPI_Trd>(ProcessTradeCallback));
				tradeApi = api;
			}

			try
			{
				bool started = api.InitConnect(configuration.Host, checked((ushort)configuration.Port), false);
				if (!started)
				{
					return new MoomooHealthResult(false, false, 0, "OpenD connection attempt could not be started.");
				}

				Task timeoutTask = Task.Delay(HealthTimeout, cancellationToken);
				Task completedTask = await Task.WhenAny(completionSource.Task, timeoutTask).ConfigureAwait(false);
				if (completedTask == completionSource.Task)
				{
					return await completionSource.Task.ConfigureAwait(false);
				}

				cancellationToken.ThrowIfCancellationRequested();
				return new MoomooHealthResult(false, false, 0, $"OpenD health check timed out after {HealthTimeout.TotalSeconds:0} seconds.");
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

		public ValueTask DisposeAsync()
		{
			if (disposed)
			{
				return ValueTask.CompletedTask;
			}

			disposed = true;
			CloseConnection();
			GC.SuppressFinalize(this);
			return ValueTask.CompletedTask;
		}

		private void ProcessConnectionCallback(string method, object?[] args)
		{
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			lock (stateLock)
			{
				completionSource = healthCompletionSource;
			}

			if (completionSource == null)
			{
				return;
			}

			if (method == nameof(MMSPI_Conn.OnInitConnect))
			{
				long errorCode = args.Length > 1 && args[1] is long code ? code : -1;
				string description = args.Length > 2 ? args[2]?.ToString() ?? string.Empty : string.Empty;

				if (errorCode != 0)
				{
					completionSource.TrySetResult(new MoomooHealthResult(false, false, 0, BuildConnectionError(errorCode, description)));
					return;
				}

				RequestAccountList();
				return;
			}

			if (method == nameof(MMSPI_Conn.OnDisconnect))
			{
				long errorCode = args.Length > 1 && args[1] is long code ? code : -1;
				completionSource.TrySetResult(
					new MoomooHealthResult(false, false, 0, $"OpenD disconnected before the health check completed (code {errorCode})."));
			}
		}

		private void ProcessTradeCallback(string method, object?[] args)
		{
			if (method != nameof(MMSPI_Trd.OnReply_GetAccList) || args.Length < 3)
			{
				return;
			}

			if (args[1] is not uint serialNo || args[2] is not TrdGetAccList.Response response)
			{
				return;
			}

			HandleAccountList(serialNo, response);
		}

		private void RequestAccountList()
		{
			MMAPI_Trd? api;
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			lock (stateLock)
			{
				api = tradeApi;
				completionSource = healthCompletionSource;
			}

			if (api == null || completionSource == null)
			{
				return;
			}

			try
			{
				TrdGetAccList.C2S c2s = TrdGetAccList.C2S.CreateBuilder()
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
			catch (Exception ex)
			{
				completionSource.TrySetResult(
					new MoomooHealthResult(true, false, 0, $"OpenD account discovery could not be started: {ex.Message}"));
			}
		}

		private void HandleAccountList(uint serialNo, TrdGetAccList.Response response)
		{
			TaskCompletionSource<MoomooHealthResult>? completionSource;
			uint expectedSequence;

			lock (stateLock)
			{
				completionSource = healthCompletionSource;
				expectedSequence = accountListSequence;
			}

			if (completionSource == null || (expectedSequence != 0 && serialNo != expectedSequence))
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

		private static T CreateProxy<T>(Action<string, object?[]> handler)
			where T : class
		{
			T proxy = DispatchProxy.Create<T, MoomooSpiProxy>();
			((MoomooSpiProxy)(object)proxy).Handler = handler;
			return proxy;
		}

		private static string BuildConnectionError(long errCode, string description)
		{
			return string.IsNullOrWhiteSpace(description)
				? $"Unable to connect to OpenD (code {errCode})."
				: $"Unable to connect to OpenD (code {errCode}): {description}";
		}

		private static void EnsureApiInitialized()
		{
			lock (ApiInitializationLock)
			{
				if (apiInitialized)
				{
					return;
				}

				MMAPI.Init();
				apiInitialized = true;
			}
		}
	}

	internal class MoomooSpiProxy : DispatchProxy
	{
		public Action<string, object?[]>? Handler { get; set; }

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (targetMethod != null)
			{
				Handler?.Invoke(targetMethod.Name, args ?? []);
			}

			return null;
		}
	}
}
