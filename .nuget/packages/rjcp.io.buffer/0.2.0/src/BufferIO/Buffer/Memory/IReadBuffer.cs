namespace RJCP.IO.Buffer.Memory
{
    using System;
    using System.Threading;

    /// <summary>
    /// The <see cref="IReadBuffer"/> allows writing to the memory region that the user reads from
    /// <see cref="IReadBufferStream"/>.
    /// </summary>
    /// <remarks>
    /// The Read Buffer is an object that the user will want to read from, so the operations in this interface are set
    /// so that a low level component can write to the buffer.
    /// </remarks>
    public interface IReadBuffer
    {
        /// <summary>
        /// The offset into <see cref="Buffer"/> where data can be written to.
        /// </summary>
        /// <value>The offset into <see cref="Buffer"/> that data can be written to.</value>
        int BufferEnd { get; }

        /// <summary>
        /// Gets the length of contiguous data that can be written to <see cref="Buffer"/>.
        /// </summary>
        /// <value>The length of contiguous data that can be written to <see cref="Buffer"/>.</value>
        int BufferWriteLength { get; }

        /// <summary>
        /// Gets a pointer to the memory that data can be written to, useful for low level API to write directly.
        /// </summary>
        /// <value>The pointer to the memory that data can be written to.</value>
        IntPtr BufferPtr { get; }

        /// <summary>
        /// Gets the write buffer.
        /// </summary>
        /// <value>The write buffer.</value>
        byte[] Buffer { get; }

#if NETSTANDARD
        /// <summary>
        /// Gets a Span for the memory region that can be written to.
        /// </summary>
        /// <value>The Span for the memory region that can be written to.</value>
        /// <remarks>
        /// On .NET Core 2.1 and later, use this property instead of <see cref="Buffer"/>, <see cref="BufferEnd"/>, <see cref="BufferWriteLength"/>
        /// <see cref="BufferPtr"/>. It's much simpler and safer.
        /// </remarks>
        Span<byte> BufferSpan { get; }
#endif

        /// <summary>
        /// Gets a value indicating whether the write buffer is empty.
        /// </summary>
        /// <value>Is <see langword="true"/> if the write buffer is not full; <see langword="false"/> otherwise.</value>
        /// <remarks>
        /// This property can be used to quickly test if there is space to write data to the write buffer. If there is
        /// free space in the write buffer, then there is no reason to wait for it and low level API can be called to
        /// fill data in the write buffer.
        /// </remarks>
        bool IsBufferNotFull { get; }

        /// <summary>
        /// Gets a wait handle that indicates that data can be written to the buffer.
        /// </summary>
        /// <value>A wait handle that low level code can wait on when the buffer is no longer full.</value>
        WaitHandle BufferNotFull { get; }

        /// <summary>
        /// Indicates that data has been written to the array.
        /// </summary>
        /// <param name="length">The amount of data that was written to the array.</param>
        /// <remarks>
        /// Data is written to the array <see cref="Buffer"/>, starting at <see cref="BufferEnd"/>, and not
        /// more than <see cref="BufferWriteLength"/>.
        /// </remarks>
        void Produce(int length);

        /// <summary>
        /// Gets the lock object for driver modifications.
        /// </summary>
        /// <value>The lock object for low level API.</value>
        /// <remarks>
        /// When low level code needs to access the read buffer via <see cref="IReadBuffer"/>, it should first take this
        /// lock, so that user code is synchronized. The implementations intended for user code (such as
        /// <see cref="IReadBufferStream"/>) automatically takes this lock as needed.
        /// </remarks>
        object Lock { get; }

        /// <summary>
        /// Indicates the underlying driver has a problem, so that there are no wait timeouts.
        /// </summary>
        void DeviceDead();

        /// <summary>
        /// Gets a value indicating if the buffer has been told that the device is dead.
        /// </summary>
        /// <value><see langword="true"/> if this instance device is dead; otherwise, <see langword="false"/>.</value>
        bool IsDeviceDead { get; }
    }
}
