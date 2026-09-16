using System.Text.Json;
using GhostfolioSidekick.Configuration;
using GhostfolioSidekick.Parsers.Moomoo;

string? host = args.ElementAtOrDefault(0);
string? portText = args.ElementAtOrDefault(1);

if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portText, out int port))
{
	Console.Error.WriteLine("Usage: MoomooHealthCheck <host> <port>");
	return 64;
}

var configuration = new MoomooConfiguration
{
	Name = "Health Check",
	Host = host,
	Port = port,
};

await using var client = new MoomooSdkClient();
MoomooHealthResult result = await client.CheckHealth(configuration);

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
{
	WriteIndented = true
}));

return result.Connected && result.RealAccountDetected ? 0 : 2;
