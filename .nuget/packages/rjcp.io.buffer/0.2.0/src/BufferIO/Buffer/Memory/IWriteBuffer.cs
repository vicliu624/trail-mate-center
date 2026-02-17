namespace RJCP.IO.Buffer.Memory
{
    using System;
    using System.Threading;

    /// <summary>
    /// The <see cref="IWriteBuffer"/> allows writing to the memory region that the user reads from
    /// <see cref="IWriteBufferStream"/>.
    /// </summary>
    public interface IWriteBuffer
    {
        /// <summary>
        /// The offset into <see cref="Buffer"/> where data can be read from.
        /// </summary>
        /// <value>The offset into <see cref="Buffer"/> that data can be read from.</value>
        int BufferStart { get; }

        /// <summary>
        /// Gets the length of contiguous data that can be read from <see cref="Buffer"/>.
        /// </summary>
        /// <value>The length of contiguous data that can be read from <see cref="Buffer"/>.</value>
        int BufferReadLength { get; }

        /// <summary>
        /// Gets a pointer to the memory that data can be read from, useful for low level API to read directly from the
        /// buffer, e.g. to write somewhere else.
        /// </summary>
        /// <value>The pointer to the memory that data can be read from.</value>
        IntPtr BufferPtr { get; }

        /// <summary>
        /// Gets the read buffer.
        /// </summary>
        /// <value>The read buffer.</value>
        byte[] Buffer { get; }

#if NETSTANDARD
        /// <summary>
        /// Gets a Span for the memory region that can be read from.
        /// </summary>
        /// <value>The Span for the memory region that can be read from.</value>
        /// <remarks>
        /// On .NET Core 2.1 and later, use this property instead of <see cref="Buffer"/>, <see cref="BufferStart"/>, <see cref="BufferReadLength"/>
        /// <see cref="BufferPtr"/>. It's much simpler and safer.
        /// </remarks>
        ReadOnlySpan<byte> BufferSpan { get; }
#endif

        /// <summary>
        /// Gets a value indicating whether the read buffer is not empty.
        /// </summary>
        /// <value>Is <see langword="true"/> if the read buffer has data; <see langword="false"/> otherwise.</value>
        /// <remarks>
        /// This property can be used to quickly test if there is data in the buffer or not. If there is data in the
        /// read buffer, then there is no reason to wait for it and data can be given to lower level API to write the
        /// data.
        /// </remarks>
        bool IsBufferNotEmpty { get; }

        /// <summary>
        /// Gets a wait handle that indicates if there is data put into the buffer.
        /// </summary>
        /// <value>
        /// A wait handle that indicates if the user put data into the buffer that low level code can wait on.
        /// </value>
        WaitHandle BufferNotEmpty { get; }

        /// <summary>
        /// Indicates that data has been read from the array.
        /// </summary>
        /// <param name="length">The amount of data that was read from the array that can be now discarded.</param>
        /// <remarks>
        /// Data is read from the array <see cref="Buffer"/>, starting at <see cref="BufferStart"/>, and not
        /// more than <see cref="BufferReadLength"/> bytes.
        /// </remarks>
        void Consume(int length);

        /// <summary>
        /// Gets the lock object for driver modifications.
        /// </summary>
        /// <value>The lock object for low level API.</value>
        /// <remarks>
        /// When low level code needs to access the read buffer via <see cref="IWriteBuffer"/>, it should first take this
        /// lock, so that user code is synchronized. The implementations intended for user code (such as
        /// <see cref="IWriteBufferStream"/>) automatically takes this lock as needed.
        /// </remarks>
        object Lock { get; }

        /// <summary>
        /// Gets the number of bytes in the buffer available for writing.
        /// </summary>
        /// <value>The number of bytes in the buffer available for writing.</value>
        int BytesFree { get; }

        /// <summary>
        /// Purges (clears) the output buffer.
        /// </summary>
        void Purge();

        /// <summary>
        /// Indicates the underlying driver has a problem, so that there are no wait timeouts.
        /// </summary>
        /// <remarks>
        /// Marking the write buffer as dead will purge all data still pending. It can't be written any more.
        /// </remarks>
        void DeviceDead();

        /// <summary>
        /// Gets a value indicating if the buffer has been told that the device is dead.
        /// </summary>
        /// <value><see langword="true"/> if this instance device is dead; otherwise, <see langword="false"/>.</value>
        bool IsDeviceDead { get; }
    }
}
