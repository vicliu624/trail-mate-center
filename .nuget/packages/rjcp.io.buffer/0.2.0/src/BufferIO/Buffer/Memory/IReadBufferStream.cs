namespace RJCP.IO.Buffer.Memory
{
    using System;
    using System.Threading;

#if NET45_OR_GREATER || NETSTANDARD
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// The interface for <see cref="MemoryReadBuffer" /> for stream implementations.
    /// </summary>
    public interface IReadBufferStream
    {
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        bool WaitForRead(int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        bool WaitForRead(int timeout, CancellationToken token);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        bool WaitForRead(int count, int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        bool WaitForRead(int count, int timeout, CancellationToken token);

#if NET45_OR_GREATER || NETSTANDARD
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        Task<bool> WaitForReadAsync(int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        Task<bool> WaitForReadAsync(int timeout, CancellationToken token);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        Task<bool> WaitForReadAsync(int count, int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        Task<bool> WaitForReadAsync(int count, int timeout, CancellationToken token);
#endif

        /// <summary>
        /// Performs a non-blocking read, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">The buffer to copy data into.</param>
        /// <param name="offset">The offset into the buffer to copy into.</param>
        /// <param name="count">The maximum number of bytes to copy.</param>
        /// <returns>
        /// Returns the number of bytes copied, which may be less or equal to <paramref name="count"/>.
        /// </returns>
        int Read(byte[] buffer, int offset, int count);

#if NETSTANDARD
        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number
        /// of bytes read.
        /// </summary>
        /// <param name="buffer">
        /// A region of memory. When this method returns, the contents of this region are replaced by the bytes read
        /// from the current source.
        /// </param>
        /// <returns>
        /// The total number of bytes read into the buffer. This can be less than the number of bytes allocated in the
        /// buffer if that many bytes are not currently available, or zero (0) if the end of the stream has been
        /// reached.
        /// </returns>
        int Read(Span<byte> buffer);
#endif

        /// <summary>
        /// Reads a single byte from the buffer without blocking.
        /// </summary>
        /// <returns>The byte value, or -1, if there is no data in the buffer.</returns>
        int ReadByte();

        /// <summary>
        /// Gets the number of bytes in the buffer available for reading.
        /// </summary>
        /// <value>The number of bytes in the buffer available for reading.</value>
        int BytesToRead { get; }

        /// <summary>
        /// Clears the read buffer so it is empty.
        /// </summary>
        void Clear();
    }
}
