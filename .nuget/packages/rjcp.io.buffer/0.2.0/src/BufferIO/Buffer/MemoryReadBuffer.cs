namespace RJCP.IO.Buffer
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Memory;
    using Timer;

#if NET45_OR_GREATER || NETSTANDARD
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// The <see cref="MemoryReadBuffer"/> is a fixed memory buffer that can assist with buffered I/O.
    /// </summary>
    /// <remarks>
    /// This class implements a thread-safe producer/consumer model, where the consumer is mapped to a stream for
    /// reading data, and the producer is mapped to a lower level driver for writing data.
    /// </remarks>
    public class MemoryReadBuffer : IReadBufferStream, IReadBuffer, IDisposable
    {
        private readonly bool m_IsPinned;
        private readonly object m_Lock = new object();
        private readonly CircularBuffer<byte> m_ReadBuffer;
        private readonly GCHandle m_ReadHandle;
        private volatile bool m_DeviceDead;
        private readonly ManualResetEventSlim m_BufferNotFull = new ManualResetEventSlim(true);

        // The m_ReadEvent is used to signal a change that when waiting, conditions can be checked. Conditions are
        // related to the state of m_ReadBuffer and m_DeviceDead. The event must be wrapped around a lock with m_Lock
        // when changing the state, or when checking the state and setting or resetting this event.
        private readonly ManualResetEventSlim m_ReadEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryReadBuffer"/> class for a particular size and no pinned buffers.
        /// </summary>
        /// <param name="length">The size of the buffer to allocate.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Parameter <paramref name="length"/> must be positive.
        /// </exception>
        public MemoryReadBuffer(int length) : this(length, false) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryReadBuffer"/> class for a particular size and optionally
        /// pin buffers.
        /// </summary>
        /// <param name="length">The size of the buffer to allocate.</param>
        /// <param name="pinned">If set to <see langword="true"/>, the buffers are pinned in memory.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Parameter <paramref name="length"/> must be positive.
        /// </exception>
        public MemoryReadBuffer(int length, bool pinned)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive");

            if (pinned) {
                byte[] read = new byte[length];
                m_ReadHandle = GCHandle.Alloc(read, GCHandleType.Pinned);
                m_ReadBuffer = new CircularBuffer<byte>(read, 0);
            } else {
                m_ReadBuffer = new CircularBuffer<byte>(length);
            }
            m_IsPinned = pinned;
        }

        /// <summary>
        /// Gets the read circular buffer for advanced operations.
        /// </summary>
        /// <value>The read circular buffer for advanced operations.</value>
        /// <remarks>
        /// Modifications to the buffer (any change in the size), must be followed by a subsequent call to
        /// <see cref="CheckBufferState"/>. This ensures that notifications when data can be read or written from the
        /// buffer from other threads do not unnecessarily block.
        /// </remarks>
        protected virtual CircularBuffer<byte> ReadBuffer { get { return m_ReadBuffer; } }

        #region IReadBufferStream
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        public bool WaitForRead(int timeout)
        {
            return WaitForRead(1, timeout, CancellationToken.None);
        }

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
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        public bool WaitForRead(int timeout, CancellationToken token)
        {
            return WaitForRead(1, timeout, token);
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is out of range.
        /// <para>- or -</para>
        /// <paramref name="count"/> is negative.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        /// <remarks>
        /// IF the <paramref name="count"/> requested is greater than the initialized capacity, this method will return
        /// <see langword="false"/> immediately without waiting.
        /// </remarks>
        public bool WaitForRead(int count, int timeout)
        {
            return WaitForRead(count, timeout, CancellationToken.None);
        }

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
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is out of range.
        /// <para>- or -</para>
        /// <paramref name="count"/> is negative.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        /// <remarks>
        /// IF the <paramref name="count"/> requested is greater than the initialized capacity, this method will return
        /// <see langword="false"/> immediately without waiting.
        /// </remarks>
        public bool WaitForRead(int count, int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return false;

            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count may not be negative");
            if (count == 0) return true;
            if (count > m_ReadBuffer.Capacity) return false;

            lock (m_Lock) {
                if (m_ReadBuffer.Length >= count) return true;
                if (timeout == 0) return false;
                if (m_DeviceDead) return false;
                m_ReadEvent.Reset();
            }

            return WaitForReadInternal(count, timeout, token);
        }

#if NET45_OR_GREATER || NETSTANDARD
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds for data to be available to read.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least one byte is now available for read, <see langword="false"/> if there was
        /// a timeout and no data is available to read.
        /// </returns>
        public Task<bool> WaitForReadAsync(int timeout)
        {
            return WaitForReadAsync(1, timeout, CancellationToken.None);
        }

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
        public Task<bool> WaitForReadAsync(int timeout, CancellationToken token)
        {
            return WaitForReadAsync(1, timeout, token);
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to read at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to read.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for read,
        /// <see langword="false"/> if there was a timeout and no data, or insufficient data, is available to read.
        /// </returns>
        public Task<bool> WaitForReadAsync(int count, int timeout)
        {
            return WaitForReadAsync(count, timeout, CancellationToken.None);
        }

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
        public Task<bool> WaitForReadAsync(int count, int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return Task.FromResult(false);

            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count may not be negative");
            if (count == 0) return Task.FromResult(true);
            if (count > m_ReadBuffer.Capacity) return Task.FromResult(false);

            lock (m_Lock) {
                if (m_ReadBuffer.Length >= count) return Task.FromResult(true);
                if (timeout == 0) return Task.FromResult(false);
                if (m_DeviceDead) return Task.FromResult(false);
                m_ReadEvent.Reset();
            }

            return WaitForReadInternalAsync(count, timeout, token);
        }

        private async Task<bool> WaitForReadInternalAsync(int count, int timeout, CancellationToken token)
        {
            try {
                return await Task.Run(() => WaitForReadInternal(count, timeout, token), token);
            } catch (TaskCanceledException) {
                return false;
            }
        }
#endif

        private bool WaitForReadInternal(int count, int timeout, CancellationToken token)
        {
            TimerExpiry timer = new TimerExpiry(timeout);
            int realTimeout = timeout;
            do {
                try {
                    if (!m_ReadEvent.Wait(realTimeout, token))
                        return false;
                } catch (OperationCanceledException) {
                    return false;
                }
                lock (m_Lock) {
                    if (token.IsCancellationRequested) return false;
                    if (m_ReadBuffer.Length >= count) return true;
                    if (m_DeviceDead) return false;
                    m_ReadEvent.Reset();
                }
                realTimeout = timer.RemainingTime();
            } while (realTimeout != 0);
            return false;
        }

        /// <summary>
        /// Performs a non-blocking read, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">The buffer to copy data into.</param>
        /// <param name="offset">The offset into the buffer to copy into.</param>
        /// <param name="count">The maximum number of bytes to copy.</param>
        /// <returns>
        /// Returns the number of bytes copied, which may be less or equal to <paramref name="count"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="buffer"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="offset"/> may not be negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="offset"/> and <paramref name="count"/> would exceed <paramref name="buffer"/> length.
        /// </exception>
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (m_Lock) {
                int bytes = m_ReadBuffer.MoveTo(buffer, offset, count);
                if (bytes > 0) m_BufferNotFull.Set();
                OnRead(bytes);
                return bytes;
            }
        }

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
        public int Read(Span<byte> buffer)
        {
            lock (m_Lock) {
                int bytes = m_ReadBuffer.MoveTo(buffer);
                if (bytes > 0) m_BufferNotFull.Set();
                OnRead(bytes);
                return bytes;
            }
        }
#endif

        /// <summary>
        /// Reads a single byte from the buffer without blocking.
        /// </summary>
        /// <returns>The byte value, or -1, if there is no data in the buffer.</returns>
        public int ReadByte()
        {
            lock (m_Lock) {
                if (m_ReadBuffer.Length == 0) return -1;
                int v = m_ReadBuffer[0];
                m_ReadBuffer.Consume(1);
                m_BufferNotFull.Set();
                OnRead(1);
                return v;
            }
        }

        /// <summary>
        /// Called when a read operation is finished, that derived classes can perform additional actions.
        /// </summary>
        /// <param name="bytes">The number of bytes that were just read and consumed.</param>
        protected virtual void OnRead(int bytes) { }

        /// <summary>
        /// Gets the number of bytes in the buffer available for reading.
        /// </summary>
        /// <value>The number of bytes in the buffer available for reading.</value>
        public int BytesToRead
        {
            get
            {
                lock (m_Lock) {
                    return m_ReadBuffer.Length;
                }
            }
        }

        /// <summary>
        /// Clears the read buffer so it is empty.
        /// </summary>
        public void Clear()
        {
            lock (m_Lock) {
                m_ReadBuffer.Consume(m_ReadBuffer.Length);
                m_BufferNotFull.Set();
            }
        }
        #endregion

        #region IReadBuffer
        /// <summary>
        /// The offset into <see cref="Buffer"/> where data can be written to.
        /// </summary>
        /// <value>The offset into <see cref="Buffer"/> that data can be written to.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public int BufferEnd
        {
            get { return m_ReadBuffer.End; }
        }

        /// <summary>
        /// Gets the length of contiguous data that can be written to <see cref="Buffer"/>.
        /// </summary>
        /// <value>The length of contiguous data that can be written to <see cref="Buffer"/>.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public int BufferWriteLength
        {
            get { return m_ReadBuffer.WriteLength; }
        }

        /// <summary>
        /// Gets a pointer to the memory that data can be written to, useful for low level API to write directly.
        /// </summary>
        /// <value>The pointer to the memory that data can be written to.</value>
        /// <remarks>
        /// This property is only valid if this object was initialized with a pinned array in
        /// <see cref="MemoryReadBuffer(int, bool)"/>.
        /// <para>This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.</para>
        /// </remarks>
        public IntPtr BufferPtr
        {
            get
            {
                return m_IsPinned ?
                    m_ReadHandle.AddrOfPinnedObject() + m_ReadBuffer.End :
                    IntPtr.Zero;
            }
        }

        /// <summary>
        /// Gets the write buffer.
        /// </summary>
        /// <value>The write buffer.</value>
        public byte[] Buffer
        {
            get { return m_ReadBuffer.Array; }
        }

#if NETSTANDARD2_1_OR_GREATER
        /// <summary>
        /// Gets a Span for the memory region that can be written to.
        /// </summary>
        /// <value>The Span for the memory region that can be written to.</value>
        /// <remarks>
        /// On .NET Core 2.1 and later, use this property instead of <see cref="Buffer"/>, <see cref="BufferEnd"/>, <see cref="BufferWriteLength"/>
        /// <see cref="BufferPtr"/>. It's much simpler and safer.
        /// </remarks>
        public Span<byte> BufferSpan
        {
            get
            {
                return new Span<byte>(m_ReadBuffer.Array, m_ReadBuffer.End, m_ReadBuffer.WriteLength);
            }
        }
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
        public bool IsBufferNotFull
        {
            get { return m_BufferNotFull.IsSet; }
        }

        /// <summary>
        /// Gets a wait handle that indicates that data can be written to the buffer.
        /// </summary>
        /// <value>A wait handle that low level code can wait on when the buffer is no longer full.</value>
        public WaitHandle BufferNotFull
        {
            get { return m_BufferNotFull.WaitHandle; }
        }

        /// <summary>
        /// Indicates that data has been written to the array.
        /// </summary>
        /// <param name="length">The amount of data that was written to the array.</param>
        /// <remarks>
        /// Data is written to the array <see cref="Buffer"/>, starting at <see cref="BufferEnd"/>, and not more than
        /// <see cref="BufferWriteLength"/>.
        /// <para>This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Cannot produce negative <paramref name="length"/>, or producing <paramref name="length"/> exceeds the amount
        /// of free space.
        /// </exception>
        public void Produce(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be positive");
            if (length == 0) return;

            m_ReadBuffer.Produce(length);
            m_ReadEvent.Set();
            if (m_ReadBuffer.Free == 0) m_BufferNotFull.Reset();
        }

        /// <summary>
        /// Gets the lock object for driver modifications.
        /// </summary>
        /// <value>The lock object for low level API.</value>
        /// <remarks>
        /// When low level code needs to access the read buffer via <see cref="IReadBuffer"/>, it should first take this
        /// lock, so that user code is synchronized. The implementations intended for user code (such as
        /// <see cref="IReadBufferStream"/>) automatically takes this lock as needed.
        /// </remarks>
        public object Lock
        {
            get { return m_Lock; }
        }

        /// <summary>
        /// Indicates the underlying driver has a problem, so that there are no wait timeouts.
        /// </summary>
        public void DeviceDead()
        {
            lock (m_Lock) {
                m_DeviceDead = true;
                m_ReadEvent.Set();
            }
        }

        /// <summary>
        /// Gets a value indicating if the buffer has been told that the device is dead.
        /// </summary>
        /// <value><see langword="true"/> if this instance device is dead; otherwise, <see langword="false"/>.</value>
        public bool IsDeviceDead
        {
            get { return m_DeviceDead; }
        }
        #endregion

        /// <summary>
        /// Checks the <see cref="ReadBuffer"/> and updates internal state.
        /// </summary>
        /// <param name="readEvent">
        /// Set to <see langword="true"/> if a read event has occurred, i.e. if new data as been added to the buffer.
        /// This causes the <see cref="WaitForRead(int)"/> methods to recheck for data.
        /// </param>
        /// <remarks>
        /// Any modifications to the allocated space of <see cref="ReadBuffer"/> directly, must result in a call to this
        /// method to ensure the state of this object is consistent to avoid unexpected blocking behaviour. It checks
        /// the buffer if data can now be read or written to the buffer.
        /// </remarks>
        protected void CheckBufferState(bool readEvent)
        {
            if (readEvent) m_ReadEvent.Set();
            lock (Lock) {
                if (m_ReadBuffer.Free == 0) {
                    m_BufferNotFull.Reset();
                } else {
                    m_BufferNotFull.Set();
                }
            }
        }

        /// <summary>
        /// Resets this instance to the empty state.
        /// </summary>
        public void Reset()
        {
            lock (m_Lock) {
                m_DeviceDead = false;
                m_ReadBuffer.Reset();
                m_ReadEvent.Reset();
                m_BufferNotFull.Set();
                OnReset();
            }
        }

        /// <summary>
        /// Called when <see cref="Reset"/> is requested.
        /// </summary>
        /// <remarks>
        /// Allows a safe way that derived classes can reset their state.
        /// </remarks>
        protected virtual void OnReset() { }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting managed and unmanaged
        /// resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool m_IsDisposed;

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release
        /// only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;

            if (disposing) {
                if (m_IsPinned) m_ReadHandle.Free();

                // We know these objects already have their underlying WaitHandle already created, so disposing of them
                // here will not result in a race condition where the WaitHandle is automatically created through a parallel call
                // to any Wait function calls.

                DeviceDead();
                m_ReadEvent.Dispose();

                // These objects aren't waited on from user code, and are expected that are stopped, so that there are
                // no potential race conditions with the underlying WaitHandle being instantiated while disposing.

                m_BufferNotFull.Set();         // In case anyone is waiting, allow them to exit. Otherwise they'd deadlock.
                m_BufferNotFull.Dispose();
            }
        }
    }
}
