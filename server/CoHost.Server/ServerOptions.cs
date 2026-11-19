using Npgsql;

namespace CoHost.Server;

/// <summary>Server settings read from COHOST_* environment variables (D11).</summary>
public sealed class ServerOptions
{
    public const string DefaultDbPassword = "cohost-change-me";
    public const int DefaultPort = 8443;

    public int Port { get; init; } = DefaultPort;

    public string DataDir { get; init; } = "/data";

    public IReadOnlyList<string> Hostnames { get; init; } = [];

    public string ConnectionString { get; init; } = string.Empty;

    public bool UsesDefaultDbPassword
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                return false;
            }

            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            return string.Equals(builder.Password, DefaultDbPassword, StringComparison.Ordinal);
        }
    }

    public static ServerOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var port = DefaultPort;
        var rawPort = configuration["COHOST_PORT"];
        if (!string.IsNullOrWhiteSpace(rawPort))
        {
            if (!int.TryParse(rawPort, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)
                || port is < 1 or > 65535)
            {
                throw new InvalidOperationException("COHOST_PORT must be an integer between 1 and 65535.");
            }
        }

        var dataDir = configuration["COHOST_DATA_DIR"];
        var hostnames = (configuration["COHOST_HOSTNAMES"] ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return new ServerOptions
        {
            Port = port,
            DataDir = string.IsNullOrWhiteSpace(dataDir) ? "/data" : dataDir,
            Hostnames = hostnames,
            ConnectionString = configuration["COHOST_DB"] ?? string.Empty,
        };
    }
}
