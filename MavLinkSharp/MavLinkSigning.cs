using System;
using System.Security.Cryptography;

namespace MavLinkSharp
{
    /// <summary>
    /// Provides MAVLink 2 signing support using HMAC-SHA256.
    /// </summary>
    public class MavLinkSigning
    {
        /// <summary>
        /// The signing flag bit for incompatibility flags.
        /// </summary>
        public const byte SigningFlag = 0x01;

        /// <summary>
        /// The length of the secret key in bytes (32 bytes = 256 bits).
        /// </summary>
        public const int SecretKeyLength = 32;

        /// <summary>
        /// The total length of the signature in bytes (13 bytes: 1 link ID + 6 timestamp + 6 truncated HMAC).
        /// </summary>
        public const int SignatureLength = 13;

        /// <summary>
        /// The length of the timestamp field in bytes.
        /// </summary>
        public const int TimestampLength = 6;

        /// <summary>
        /// The length of the truncated hash in bytes.
        /// </summary>
        public const int TruncatedHashLength = 6;

        /// <summary>
        /// The default timestamp window in microseconds (10 seconds).
        /// </summary>
        public const long DefaultTimestampWindow = 10_000_000;

        private readonly byte[] _secretKey;
        private readonly object _stateLock = new object();
        private long _timestamp;
        private byte _linkId;
        private bool _acceptTimestampsBeforeTimestamp;

        /// <summary>
        /// Gets or sets the link ID used for signing.
        /// </summary>
        public byte LinkId
        {
            get { lock (_stateLock) { return _linkId; } }
            set { lock (_stateLock) { _linkId = value; } }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to accept timestamps before the current timestamp.
        /// </summary>
        public bool AcceptTimestampsBeforeTimestamp
        {
            get { lock (_stateLock) { return _acceptTimestampsBeforeTimestamp; } }
            set { lock (_stateLock) { _acceptTimestampsBeforeTimestamp = value; } }
        }

        /// <summary>
        /// Gets the secret key used for signing.
        /// </summary>
        public ReadOnlySpan<byte> SecretKey => _secretKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="MavLinkSigning"/> class with a secret key.
        /// </summary>
        /// <param name="secretKey">The 32-byte secret key.</param>
        /// <exception cref="ArgumentException">Thrown when secretKey is null or not exactly 32 bytes.</exception>
        public MavLinkSigning(byte[] secretKey)
        {
            if (secretKey == null || secretKey.Length != SecretKeyLength)
                throw new ArgumentException($"Secret key must be exactly {SecretKeyLength} bytes.", nameof(secretKey));

            _secretKey = new byte[SecretKeyLength];
            Array.Copy(secretKey, _secretKey, SecretKeyLength);

            _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            _timestamp &= 0xFFFFFFFFFFFF;
            _linkId = 0;
            _acceptTimestampsBeforeTimestamp = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MavLinkSigning"/> class with a passphrase.
        /// The passphrase is hashed using SHA-256 to generate the secret key.
        /// </summary>
        /// <param name="passphrase">The passphrase to derive the secret key from.</param>
        /// <exception cref="ArgumentException">Thrown when passphrase is null or empty.</exception>
        public MavLinkSigning(string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException("Passphrase cannot be null or empty.", nameof(passphrase));

            _secretKey = new byte[SecretKeyLength];
            using (var sha256 = SHA256.Create())
            {
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(passphrase);
                var hash = sha256.ComputeHash(keyBytes);
                Array.Copy(hash, _secretKey, SecretKeyLength);
            }

            _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            _timestamp &= 0xFFFFFFFFFFFF;
            _linkId = 0;
            _acceptTimestampsBeforeTimestamp = false;
        }

        /// <summary>
        /// Generates a signature for the specified packet using the given link ID and timestamp.
        /// </summary>
        /// <param name="packet">The packet data to sign (header without start marker + payload + checksum).</param>
        /// <param name="linkId">The link ID to use for signing.</param>
        /// <param name="timestamp">The timestamp to use for signing (microseconds since Unix epoch).</param>
        /// <returns>A 13-byte signature.</returns>
        public byte[] GenerateSignature(ReadOnlySpan<byte> packet, byte linkId, long timestamp)
        {
            var signature = new byte[SignatureLength];
            signature[0] = linkId;

            for (int i = 0; i < TimestampLength; i++)
            {
                signature[1 + i] = (byte)(timestamp >> (i * 8));
            }

            var dataToSign = new byte[1 + TimestampLength + packet.Length];
            dataToSign[0] = linkId;
            for (int i = 0; i < TimestampLength; i++)
            {
                dataToSign[1 + i] = (byte)(timestamp >> (i * 8));
            }
            packet.CopyTo(dataToSign.AsSpan(1 + TimestampLength));

            using (var hmac = new HMACSHA256(_secretKey))
            {
                var hash = hmac.ComputeHash(dataToSign);
                Array.Copy(hash, 0, signature, 1 + TimestampLength, TruncatedHashLength);
            }

            return signature;
        }

        /// <summary>
        /// Generates a signature for the specified packet using the current link ID and an auto-incremented timestamp.
        /// </summary>
        /// <param name="packet">The packet data to sign (header without start marker + payload + checksum).</param>
        /// <returns>A 13-byte signature.</returns>
        public byte[] GenerateSignature(ReadOnlySpan<byte> packet)
        {
            lock (_stateLock)
            {
                _timestamp = (_timestamp + 1) & 0xFFFFFFFFFFFF;
                return GenerateSignature(packet, _linkId, _timestamp);
            }
        }

        /// <summary>
        /// Validates a signature against the specified packet.
        /// </summary>
        /// <param name="packet">The packet data that was signed.</param>
        /// <param name="signature">The signature to validate.</param>
        /// <param name="timestampWindow">The timestamp window in microseconds (default: 10 seconds).</param>
        /// <returns>True if the signature is valid; otherwise, false.</returns>
        public bool ValidateSignature(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> signature, long timestampWindow = DefaultTimestampWindow)
        {
            if (signature.Length < SignatureLength)
                return false;

            var linkId = signature[0];
            long timestamp = 0;
            for (int i = 0; i < TimestampLength; i++)
            {
                timestamp |= (long)signature[1 + i] << (i * 8);
            }

            if (!ValidateTimestamp(timestamp, timestampWindow))
                return false;

            var expectedSignature = GenerateSignatureForValidation(packet, linkId, timestamp);
            for (int i = 0; i < TruncatedHashLength; i++)
            {
                if (signature[1 + TimestampLength + i] != expectedSignature[1 + TimestampLength + i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Generates a signature for validation purposes (internal use).
        /// </summary>
        private byte[] GenerateSignatureForValidation(ReadOnlySpan<byte> packet, byte linkId, long timestamp)
        {
            var signature = new byte[SignatureLength];
            signature[0] = linkId;

            for (int i = 0; i < TimestampLength; i++)
            {
                signature[1 + i] = (byte)(timestamp >> (i * 8));
            }

            var dataToSign = new byte[1 + TimestampLength + packet.Length];
            dataToSign[0] = linkId;
            for (int i = 0; i < TimestampLength; i++)
            {
                dataToSign[1 + i] = (byte)(timestamp >> (i * 8));
            }
            packet.CopyTo(dataToSign.AsSpan(1 + TimestampLength));

            using (var hmac = new HMACSHA256(_secretKey))
            {
                var hash = hmac.ComputeHash(dataToSign);
                Array.Copy(hash, 0, signature, 1 + TimestampLength, TruncatedHashLength);
            }

            return signature;
        }

        /// <summary>
        /// Validates whether a timestamp is within the acceptable window.
        /// </summary>
        /// <param name="timestamp">The timestamp to validate (microseconds since Unix epoch).</param>
        /// <param name="timestampWindow">The timestamp window in microseconds (default: 10 seconds).</param>
        /// <returns>True if the timestamp is within the window; otherwise, false.</returns>
        public bool ValidateTimestamp(long timestamp, long timestampWindow = DefaultTimestampWindow)
        {
            var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            currentTimestamp &= 0xFFFFFFFFFFFF;

            long diff = currentTimestamp - timestamp;
            if (diff < 0)
            {
                // Timestamp is in the future, check if it's within window
                diff = timestamp - currentTimestamp;
            }

            if (diff > timestampWindow)
                return false;

            return true;
        }

        /// <summary>
        /// Gets the current timestamp and increments the internal timestamp counter.
        /// </summary>
        /// <returns>The current timestamp in microseconds since Unix epoch.</returns>
        public long GetCurrentTimestamp()
        {
            lock (_stateLock)
            {
                _timestamp = (_timestamp + 1) & 0xFFFFFFFFFFFF;
                return _timestamp;
            }
        }

        /// <summary>
        /// Creates a random 32-byte secret key.
        /// </summary>
        /// <returns>A 32-byte random key.</returns>
        public static byte[] CreateRandomKey()
        {
            var key = new byte[SecretKeyLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            return key;
        }
    }
}
