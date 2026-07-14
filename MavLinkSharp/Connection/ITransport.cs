using System;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// Defines the contract for a byte-level transport layer used by <see cref="MavLinkConnection"/>.
    /// Implementations provide send/receive capabilities over a specific communication channel (UDP, TCP, Serial, etc.).
    /// </summary>
    public interface ITransport
    {
        /// <summary>
        /// Gets whether the transport is currently connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Establishes the transport connection.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the transport connection.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends data over the transport.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes sent.</returns>
        Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives data from the transport into the provided buffer.
        /// </summary>
        /// <param name="buffer">The buffer to receive data into.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes received.</returns>
        Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously releases the transport resources.
        /// </summary>
        ValueTask DisposeAsync();
    }
}
