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
    /// The <see cref="MemoryWriteBuffer"/> is a fixed memory buffer that can assist with buffered I/O.
    /// </summary>
    /// <remarks>
    /// This class implements a thread-safe producer/consumer model, where the producer is mapped to a stream for
    /// writing data, and the consume is mapped to a lower level driver for reading data.
    /// </remarks>
    public class MemoryWriteBuffer : IWriteBufferStream, IWriteBuffer, IDisposable
    {
        private readonly bool m_IsPinned;
        private readonly object m_Lock = new object();
        private readonly CircularBuffer<byte> m_WriteBuffer;
        private readonly GCHandle m_WriteHandle;
        private volatile bool m_DeviceDead;
        private readonly ManualResetEventSlim m_BufferNotEmpty = new ManualResetEventSlim(false);

        // The m_WriteEvent is used to signal a change that when waiting, conditions can be checked. Conditions are
        // related to the state of m_WriteBuffer and m_DeviceDead. The event must be wrapped around a lock with m_Lock
        // when changing the state, or when checking the state and setting or resetting this event.
        private readonly ManualResetEventSlim m_WriteEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryReadBuffer"/> class for a particular size and no pinned buffers.
        /// </summary>
        /// <param name="length">The size of the buffer to allocate.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Parameter <paramref name="length"/> must be positive.
        /// </exception>
        public MemoryWriteBuffer(int length) : this(length, false) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryReadBuffer"/> class for a particular size and optionally pin buffers.
        /// </summary>
        /// <param name="length">The size of the buffer to allocate.</param>
        /// <param name="pinned">If set to <see langword="true"/>, the buffers are pinned in memory.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Parameter <paramref name="length"/> must be positive.
        /// </exception>
        public MemoryWriteBuffer(int length, bool pinned)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive");

            if (pinned) {
                byte[] read = new byte[length];
                m_WriteHandle = GCHandle.Alloc(read, GCHandleType.Pinned);
                m_WriteBuffer = new CircularBuffer<byte>(read, 0);
            } else {
                m_WriteBuffer = new CircularBuffer<byte>(length);
            }
            m_IsPinned = pinned;
        }

        /// <summary>
        /// Gets the write circular buffer for advanced operations.
        /// </summary>
        /// <value>The write circular buffer for advanced operations.</value>
        protected virtual CircularBuffer<byte> WriteBuffer { get { return m_WriteBuffer; } }

        #region IWriteBufferStream
        /// <summary>
        /// Waits up to <paramref name="timeout"/> milliseconds to write at least <paramref name="count"/> bytes.
        /// </summary>
        /// <param name="count">The number of bytes to wait for to write.</param>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        public bool WaitForWrite(int count, int timeout)
        {
            return WaitForWrite(count, timeout, CancellationToken.None);
        }

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
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is out of range.
        /// <para>- or -</para>
        /// <paramref name="count"/> is negative.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This object is disposed of.</exception>
        public bool WaitForWrite(int count, int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return false;

            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count may not be negative");
            if (count == 0) return true;
            if (count > m_WriteBuffer.Capacity) return false;

            return WaitForWriteEvent(count, timeout, token);
        }

        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        public bool WaitForEmpty(int timeout)
        {
            return WaitForEmpty(timeout, CancellationToken.None);
        }

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
        public bool WaitForEmpty(int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return false;

            return WaitForWriteEvent(-1, timeout, token);
        }

        private bool WaitForWriteEvent(int count, int timeout, CancellationToken token)
        {
            lock (m_Lock) {
                if (m_DeviceDead) return false;
                if (WaitForWriteEventCondition(count)) return true;
                if (timeout == 0) return false;
                m_WriteEvent.Reset();
            }
            return WaitForWriteEventInternal(count, timeout, token);
        }

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
        public Task<bool> WaitForWriteAsync(int count, int timeout)
        {
            return WaitForWriteAsync(count, timeout, CancellationToken.None);
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
        /// <see langword="true"/> if at least <paramref name="count"/> bytes are now available for writing,
        /// <see langword="false"/> if there was a timeout and insufficient space is available to write.
        /// </returns>
        public Task<bool> WaitForWriteAsync(int count, int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return Task.FromResult(false);

            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count may not be negative");
            if (count == 0) return Task.FromResult(true);
            if (count > m_WriteBuffer.Capacity) return Task.FromResult(false);

            return WaitForWriteEventAsync(count, timeout, token);
        }

        /// <summary>
        /// Waits for the write buffer to become empty.
        /// </summary>
        /// <param name="timeout">The timeout to wait for, in milliseconds.</param>
        /// <returns>
        /// <see langword="true"/> if the buffer became completely empty while waiting, <see langword="false"/> if there
        /// was a timeout and data still remains to write.
        /// </returns>
        public Task<bool> WaitForEmptyAsync(int timeout)
        {
            return WaitForEmptyAsync(timeout, CancellationToken.None);
        }

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
        public Task<bool> WaitForEmptyAsync(int timeout, CancellationToken token)
        {
            if (timeout < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            if (token.IsCancellationRequested) return Task.FromResult(false);

            return WaitForWriteEventAsync(-1, timeout, token);
        }

        private Task<bool> WaitForWriteEventAsync(int count, int timeout, CancellationToken token)
        {
            lock (m_Lock) {
                if (m_DeviceDead) return Task.FromResult(false);
                if (WaitForWriteEventCondition(count)) return Task.FromResult(true);
                if (timeout == 0) return Task.FromResult(false);
                m_WriteEvent.Reset();
            }
            return WaitForWriteEventInternalAsync(count, timeout, token);
        }

        private async Task<bool> WaitForWriteEventInternalAsync(int count, int timeout, CancellationToken token)
        {
            try {
                return await Task.Run(() => WaitForWriteEventInternal(count, timeout, token), token);
            } catch (TaskCanceledException) {
                return false;
            }
        }
#endif

        private bool WaitForWriteEventInternal(int count, int timeout, CancellationToken token)
        {
            TimerExpiry timer = new TimerExpiry(timeout);
            int realTimeout = timeout;
            do {
                try {
                    if (!m_WriteEvent.Wait(realTimeout, token))
                        return false;
                } catch (OperationCanceledException) {
                    return false;
                }
                lock (m_Lock) {
                    if (token.IsCancellationRequested) return false;
                    if (m_DeviceDead) return false;
                    if (WaitForWriteEventCondition(count)) return true;
                    m_WriteEvent.Reset();
                }
                realTimeout = timer.RemainingTime();
            } while (realTimeout != 0);
            return false;
        }

        private bool WaitForWriteEventCondition(int count)
        {
            return (count < 0) ?
                m_WriteBuffer.Length == 0 :     // If count < 0, return true if empty
                m_WriteBuffer.Free >= count;    // If count >= 0, return true if enough bytes
        }

        /// <summary>
        /// Performs a non-blocking write, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">The buffer to copy data into.</param>
        /// <param name="offset">The offset into the buffer to copy into.</param>
        /// <param name="count">The maximum number of bytes to copy.</param>
        /// <returns>
        /// Returns the number of bytes copied, which may be less or equal to <paramref name="count"/>.
        /// </returns>
        /// <remarks>
        /// Data may be lost if an attempt to write <paramref name="count"/> bytes exceeds the buffer size. To ensure
        /// this doesn't happen, call <see cref="WaitForWrite(int, int, CancellationToken)"/> prior.
        /// </remarks>
        public void Write(byte[] buffer, int offset, int count)
        {
            lock (m_Lock) {
                if (m_DeviceDead) return;
                m_WriteBuffer.Append(buffer, offset, count);
                m_BufferNotEmpty.Set();
                OnWrite(count);
            }
        }

#if NETSTANDARD
        /// <summary>
        /// Performs a non-blocking write, copying data from the memory buffer to the array specified.
        /// </summary>
        /// <param name="buffer">A region of memory.</param>
        public void Write(ReadOnlySpan<byte> buffer)
        {
            lock (m_Lock) {
                if (m_DeviceDead) return;
                m_WriteBuffer.Append(buffer);
                m_BufferNotEmpty.Set();
                OnWrite(buffer.Length);
            }
        }
#endif

        /// <summary>
        /// Called when the user wants to write.
        /// </summary>
        /// <param name="count">The number of bytes that were written.</param>
        /// <remarks>
        /// The write occurs from the user layer, but the driver layer may need to be notified. Override this class and
        /// provide your own implementation for notification.
        /// </remarks>
        protected virtual void OnWrite(int count) { }

        /// <summary>
        /// Gets the number of bytes in the buffer still pending for writing.
        /// </summary>
        /// <value>The number of bytes in the buffer pending for writing.</value>
        public int BytesToWrite
        {
            get
            {
                lock (m_Lock) {
                    return m_WriteBuffer.Length;
                }
            }
        }
        #endregion

        #region IWriteBuffer
        /// <summary>
        /// The offset into <see cref="Buffer"/> where data can be read from.
        /// </summary>
        /// <value>The offset into <see cref="Buffer"/> that data can be read from.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public int BufferStart
        {
            get { return m_WriteBuffer.Start; }
        }

        /// <summary>
        /// Gets the length of contiguous data that can be read from <see cref="Buffer"/>.
        /// </summary>
        /// <value>The length of contiguous data that can be read from <see cref="Buffer"/>.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public int BufferReadLength
        {
            get { return m_WriteBuffer.ReadLength; }
        }

        /// <summary>
        /// Gets a pointer to the memory that data can be read from, useful for low level API to read directly from the
        /// buffer, e.g. to write somewhere else.
        /// </summary>
        /// <value>The pointer to the memory that data can be read from.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public IntPtr BufferPtr
        {
            get
            {
                return m_IsPinned ?
                    m_WriteHandle.AddrOfPinnedObject() + m_WriteBuffer.Start :
                    IntPtr.Zero;
            }
        }

        /// <summary>
        /// Gets the read buffer.
        /// </summary>
        /// <value>The read buffer.</value>
        public byte[] Buffer
        {
            get { return m_WriteBuffer.Array; }
        }

#if NETSTANDARD2_1_OR_GREATER
        /// <summary>
        /// Gets a Span for the memory region that can be read from.
        /// </summary>
        /// <value>The Span for the memory region that can be read from.</value>
        /// <remarks>
        /// On .NET Core 2.1 and later, use this property instead of <see cref="Buffer"/>, <see cref="BufferStart"/>, <see cref="BufferReadLength"/>
        /// <see cref="BufferPtr"/>. It's much simpler and safer.
        /// </remarks>
        public ReadOnlySpan<byte> BufferSpan
        {
            get
            {
                return new Span<byte>(m_WriteBuffer.Array, m_WriteBuffer.Start, m_WriteBuffer.ReadLength);
            }
        }
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
        public bool IsBufferNotEmpty
        {
            get { return m_BufferNotEmpty.IsSet; }
        }

        /// <summary>
        /// Gets a wait handle that indicates if there is data put into the buffer.
        /// </summary>
        /// <value>
        /// A wait handle that indicates if the user put data into the buffer that low level code can wait on.
        /// </value>
        public WaitHandle BufferNotEmpty
        {
            get { return m_BufferNotEmpty.WaitHandle; }
        }

        /// <summary>
        /// Indicates that data has been read from the array.
        /// </summary>
        /// <param name="length">The amount of data that was read from the array that can be now discarded.</param>
        /// <remarks>
        /// Data is read from the array <see cref="Buffer"/>, starting at <see cref="BufferStart"/>, and not
        /// more than <see cref="BufferReadLength"/> bytes.
        /// <para>This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.</para>
        /// </remarks>
        public void Consume(int length)
        {
            m_WriteBuffer.Consume(length);
            m_WriteEvent.Set();
            if (m_WriteBuffer.Length == 0) m_BufferNotEmpty.Reset();
        }

        /// <summary>
        /// Gets the lock object for driver modifications.
        /// </summary>
        /// <value>The lock object for low level API.</value>
        /// <remarks>
        /// When low level code needs to access the read buffer via <see cref="IWriteBuffer"/>, it should first take this
        /// lock, so that user code is synchronized. The implementations intended for user code (such as
        /// <see cref="IWriteBufferStream"/>) automatically takes this lock as needed.
        /// </remarks>
        public object Lock
        {
            get { return m_Lock; }
        }

        /// <summary>
        /// Gets the number of bytes in the buffer available for writing.
        /// </summary>
        /// <value>The number of bytes in the buffer available for writing.</value>
        /// <remarks>
        /// This property is not thread safe and must be wrapped around a lock with <see cref="Lock"/>.
        /// </remarks>
        public int BytesFree
        {
            get { return m_WriteBuffer.Free; }
        }

        /// <summary>
        /// Purges (clears) the output buffer.
        /// </summary>
        public void Purge()
        {
            lock (m_Lock) {
                m_WriteBuffer.Reset();
                m_BufferNotEmpty.Reset();
                m_WriteEvent.Set();
            }
        }

        /// <summary>
        /// Indicates the underlying driver has a problem, so that there are no wait timeouts.
        /// </summary>
        /// <remarks>
        /// Marking the write buffer as dead will purge all data still pending. It can't be written any more.
        /// </remarks>
        public void DeviceDead()
        {
            lock (m_Lock) {
                m_DeviceDead = true;
                m_WriteBuffer.Reset();
                m_BufferNotEmpty.Reset();
                m_WriteEvent.Set();
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
        /// Resets this instance to the empty state.
        /// </summary>
        public void Reset()
        {
            lock (m_Lock) {
                m_DeviceDead = false;
                m_WriteBuffer.Reset();
                m_WriteEvent.Reset();
                m_BufferNotEmpty.Reset();
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
                if (m_IsPinned) m_WriteHandle.Free();

                // We know these objects already have their underlying WaitHandle already created, so disposing of them
                // here will not result in a race condition where the WaitHandle is automatically created through a parallel call
                // to any Wait function calls.

                DeviceDead();
                m_WriteEvent.Dispose();

                // These objects aren't waited on from user code, and are expected that are stopped, so that there are
                // no potential race conditions with the underlying WaitHandle being instantiated while disposing.

                m_BufferNotEmpty.Set();         // In case anyone is waiting, allow them to exit. Otherwise they'd deadlock.
                m_BufferNotEmpty.Dispose();
            }
        }
    }
}
