using System;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// Serial/UART transport implementation for <see cref="MavLinkConnection"/>.
    /// Wraps <see cref="SerialPort"/> for communication with MAVLink devices over serial ports.
    /// </summary>
    public class SerialTransport : ITransport
    {
        private readonly SerialPort _serialPort;
        private bool _connected;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a new serial transport with the specified port name and baud rate.
        /// </summary>
        /// <param name="portName">The serial port name (e.g., "COM3" on Windows, "/dev/ttyUSB0" on Linux).</param>
        /// <param name="baudRate">The baud rate for communication (e.g., 57600, 115200).</param>
        public SerialTransport(string portName, int baudRate)
        {
            _serialPort = new SerialPort(portName, baudRate);
        }

        /// <summary>
        /// Creates a new serial transport with full configuration.
        /// </summary>
        /// <param name="portName">The serial port name.</param>
        /// <param name="baudRate">The baud rate.</param>
        /// <param name="parity">The parity setting (default None).</param>
        /// <param name="dataBits">The data bits (default 8).</param>
        /// <param name="stopBits">The stop bits (default One).</param>
        public SerialTransport(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
        }

        /// <summary>
        /// Creates a new serial transport using an existing <see cref="SerialPort"/>.
        /// </summary>
        /// <param name="serialPort">The pre-configured serial port.</param>
        public SerialTransport(SerialPort serialPort)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get { lock (_lock) { return _connected; } }
        }

        /// <summary>
        /// Gets the underlying <see cref="SerialPort"/> instance.
        /// </summary>
        public SerialPort Port => _serialPort;

        /// <inheritdoc/>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_serialPort.IsOpen)
            {
                _serialPort.Open();
            }

            lock (_lock)
            {
                _connected = true;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _connected = false;
            }

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (!_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port is not open.");

            byte[] buffer;
            if (MemoryMarshal.TryGetArray(data, out var arraySegment))
            {
                buffer = arraySegment.Array;
            }
            else
            {
                buffer = data.ToArray();
            }

            _serialPort.Write(buffer, 0, buffer.Length);
            return Task.FromResult(buffer.Length);
        }

        /// <inheritdoc/>
        public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port is not open.");

            byte[] tempBuffer = new byte[buffer.Length];
            int bytesRead = _serialPort.Read(tempBuffer, 0, tempBuffer.Length);
            tempBuffer.AsSpan(0, bytesRead).CopyTo(buffer.Span);
            return Task.FromResult(bytesRead);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Releases the serial port resources.
        /// </summary>
        public void Dispose()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort.Dispose();
        }
    }
}
