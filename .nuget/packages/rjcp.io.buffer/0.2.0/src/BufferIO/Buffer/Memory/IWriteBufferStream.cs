namespace RJCP.IO.Buffer.Memory
{
    using System;
    using System.Threading;

#if NET45_OR_GREATER || NETSTANDARD
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// The interface for <see cref="MemoryReadBuffer"/> for stream implementations.
    /// </summary>
    public interface IWriteBufferStream
    {
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to write at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to write.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        bool WaitForWrite(int count, int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to write at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        bool WaitForWrite(int count, int timeout, CancellationToken token);

#if NET45_OR_GREATER || NETSTANDARD
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to write at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to write.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        Task<bool> WaitForWriteAsync(int count, int timeout);

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        Task<bool> WaitForWriteAsync(int count, int timeout, CancellationToken token);
#endif

        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        bool WaitForEmpty(int timeout);

        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        bool WaitForEmpty(int timeout, CancellationToken token);

#if NET45_OR_GREATER || NETSTANDARD
        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        Task<bool> WaitForEmptyAsync(int timeout);

        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <param name="token">
        /// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        Task<bool> WaitForEmptyAsync(int timeout, CancellationToken token);
#endif

        /// <summary>
        /// Performs a non-blocking write, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">The buffer to copy data into.</param>
        /// <param name="offset">The offset into the buffer to copy into.</param>
        /// <param name="count">The maximum number of bytes to copy.</param>
        /// <returns>
        /// Returns the number of bytes copied, which may be less or equal to <paramref name="count"/>.
        /// </returns>
        void Write(byte[] buffer, int offset, int count);

#if NETSTANDARD
        /// <summary>
        /// Performs a non-blocking write, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">A region of memory.</param>
        void Write(ReadOnlySpan<byte> buffer);
#endif

        /// <summary>
        /// Gets the number of bytes in the buffer still pending for writing.
        /// </summary>
        /// <value>The number of bytes in the buffer pending for writing.</value>
        int BytesToWrite { get; }
    }
}
