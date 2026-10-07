using System.Net;
using System.Net.Sockets;
using MavLinkSharp.Connection;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Creates <see cref="ITransport"/> instances from compact URI style endpoints.
/// </summary>
/// <remarks>
/// Supported endpoints:
/// <list type="bullet">
/// <item><c>udp://:14550</c> - listen on any interface, UDP port 14550.</item>
/// <item><c>udp://127.0.0.1:14550</c> - listen on a specific interface.</item>
/// <item><c>udp://:0</c> with <c>--remote host:port</c> - send and receive against a fixed peer.</item>
/// <item><c>tcp://:5760</c> - accept an incoming TCP connection.</item>
/// <item><c>tcp://host:5760</c> - connect out to a TCP peer.</item>
/// <item><c>serial://COM3:57600</c> - open a serial port.</item>
/// </list>
/// </remarks>
internal static class TransportFactory
{
    internal const int DefaultUdpPort = 14550;
    internal const int DefaultTcpPort = 5760;
    internal const int DefaultSerialBaudRate = 57600;

    /// <summary>
    /// Creates a transport for the supplied endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint URI, for example <c>udp://:14550</c>.</param>
    /// <param name="remote">Optional fixed peer for datagram transports, as <c>host:port</c>.</param>
    internal static ITransport Create(string endpoint, string? remote = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("An endpoint is required, for example 'udp://:14550'.", nameof(endpoint));
        }

        SplitScheme(endpoint, out var scheme, out var authority);

        return scheme switch
        {
            "udp" => CreateUdp(authority, remote),
            "tcp" => CreateTcp(authority),
            "serial" => CreateSerial(authority),
            _ => throw new ArgumentException(
                $"Unsupported endpoint scheme '{scheme}'. Use 'udp', 'tcp' or 'serial', for example 'udp://:14550'.")
        };
    }

    /// <summary>
    /// Creates a transport used to send to a destination, as replay needs.
    /// </summary>
    /// <param name="endpoint">A destination endpoint. For UDP the host is the peer, so <c>udp://:14550</c> is rejected.</param>
    internal static ITransport CreateSender(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("A target endpoint is required, for example 'udp://127.0.0.1:14550'.", nameof(endpoint));
        }

        SplitScheme(endpoint, out var scheme, out var authority);

        switch (scheme)
        {
            case "udp":
            {
                SplitHostPort(authority, out var host, out var port);

                if (string.IsNullOrWhiteSpace(host) || port is null)
                {
                    throw new ArgumentException(
                        $"Invalid UDP target '{endpoint}'. Use 'udp://host:port', for example 'udp://127.0.0.1:14550'.");
                }

                ValidatePort(port.Value, "UDP");

                // Local port 0 lets the OS pick an ephemeral port for replies.
                return new UdpTransport(0, new IPEndPoint(ResolveBindAddress(host), port.Value));
            }
            case "tcp":
            {
                SplitHostPort(authority, out var host, out var port);

                if (string.IsNullOrWhiteSpace(host) || port is null)
                {
                    throw new ArgumentException(
                        $"Invalid TCP target '{endpoint}'. Use 'tcp://host:port', for example 'tcp://127.0.0.1:5760'.");
                }

                ValidatePort(port.Value, "TCP");
                return new TcpTransport(host, port.Value);
            }
            case "serial":
                return CreateSerial(authority);
            default:
                throw new ArgumentException(
                    $"Unsupported target scheme '{scheme}'. Use 'udp', 'tcp' or 'serial', for example 'udp://127.0.0.1:14550'.");
        }
    }

    private static ITransport CreateUdp(string authority, string? remote)
    {
        SplitHostPort(authority, out var host, out var port);
        int localPort = port ?? DefaultUdpPort;
        var bindAddress = ResolveBindAddress(host);
        ValidatePort(localPort, "UDP");

        if (string.IsNullOrWhiteSpace(remote))
        {
            return new UdpTransport(new IPEndPoint(bindAddress, localPort));
        }

        SplitHostPort(remote, out var remoteHost, out var remotePort);
        if (string.IsNullOrWhiteSpace(remoteHost) || remotePort is null)
        {
            throw new ArgumentException($"Invalid --remote value '{remote}'. Expected 'host:port'.");
        }

        ValidatePort(remotePort.Value, "UDP");

        // The transport takes an IPEndPoint, so host names are resolved here.
        return new UdpTransport(localPort, new IPEndPoint(ResolveBindAddress(remoteHost), remotePort.Value));
    }

    private static ITransport CreateTcp(string authority)
    {
        SplitHostPort(authority, out var host, out var port);
        int tcpPort = port ?? DefaultTcpPort;
        ValidatePort(tcpPort, "TCP");

        // An empty (or wildcard) host means "wait for an inbound connection".
        if (string.IsNullOrWhiteSpace(host) || host is "*" or "+")
        {
            return new TcpTransport(new IPEndPoint(ResolveBindAddress(host), tcpPort), asServer: true);
        }

        return new TcpTransport(host, tcpPort);
    }

    private static ITransport CreateSerial(string authority)
    {
        SplitHostPort(authority, out var portName, out var baudRate);
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("A serial port name is required, for example 'serial://COM3:57600'.");
        }

        int baud = baudRate ?? DefaultSerialBaudRate;
        ValidatePort(baud, "serial baud rate");

        return new SerialTransport(portName, baud);
    }

    private static void SplitScheme(string endpoint, out string scheme, out string authority)
    {
        int separator = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException(
                $"Invalid endpoint '{endpoint}'. Expected a scheme followed by '://', for example 'udp://:14550'.");
        }

        scheme = endpoint[..separator].ToLowerInvariant();
        authority = endpoint[(separator + 3)..];
    }

    /// <summary>
    /// Splits <c>host:port</c>, <c>[ipv6]:port</c> or a bare host. A trailing slash (as used by serial paths
    /// such as <c>/dev/ttyUSB0</c>) is tolerated.
    /// </summary>
    private static void SplitHostPort(string value, out string host, out int? port)
    {
        host = value.Trim();
        port = null;

        if (host.StartsWith('['))
        {
            int closing = host.IndexOf(']');
            if (closing < 0)
            {
                throw new ArgumentException($"Invalid endpoint '{value}': unbalanced brackets.");
            }

            string bracketHost = host[1..closing];
            string remainder = host[(closing + 1)..];
            host = bracketHost;

            if (remainder.StartsWith(':'))
            {
                port = ParsePort(remainder[1..], value);
            }

            return;
        }

        int colon = host.LastIndexOf(':');
        if (colon < 0)
        {
            host = host.TrimEnd('/');
            return;
        }

        string portText = host[(colon + 1)..].Trim();
        string hostText = host[..colon].TrimEnd('/');
        host = hostText;

        if (portText.Length > 0)
        {
            port = ParsePort(portText, value);
        }
    }

    private static int ParsePort(string text, string originalValue)
    {
        if (!int.TryParse(text, out int value))
        {
            throw new ArgumentException($"Invalid port '{text}' in endpoint '{originalValue}'.");
        }

        return value;
    }

    private static void ValidatePort(int port, string kind)
    {
        if (port is < 0 or > 65535)
        {
            throw new ArgumentException($"The {kind} value {port} is outside the valid range 0-65535.");
        }
    }

    private static IPAddress ResolveBindAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host is "*" or "+" or "any")
        {
            return IPAddress.Any;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            address = Dns.GetHostAddresses(host).FirstOrDefault()
                      ?? throw new ArgumentException($"Cannot resolve host '{host}'.");
        }

        return address;
    }
}