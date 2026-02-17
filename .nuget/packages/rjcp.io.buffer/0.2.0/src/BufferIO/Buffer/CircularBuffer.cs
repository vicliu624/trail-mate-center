namespace RJCP.IO.Buffer
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A simple data structure to manage an array as a circular buffer.
    /// </summary>
    /// <remarks>
    /// This class provides simple methods for abstracting a circular buffer. A circular buffer allows for faster access
    /// of data by avoiding potential copy operations for data that is at the beginning.
    /// <para>
    /// Stream data structures can benefit from this data structure by allocating a single block on the heap of an
    /// arbitrary size. If the stream is long-lived the benefits are larger. In the .NET framework (4.0 and earlier),
    /// all allocations of data structures that are 80kb and larger are automatically allocated on the heap. The heap is
    /// not garbage collected like smaller objects. Instead, new elements are added to the heap in an incremental
    /// fashion. It is theoretically possible to exhaust all memory in an application by allocating and deallocating
    /// regularly on a heap if such a new heap element requires space and there is not a single block large enough. By
    /// using the <see cref="CircularBuffer{T}"/> with the type <c>T</c> as <c>byte</c>, you can preallocate a buffer
    /// for a stream of any reasonable size (as a simple example 5MB). That block is allocated once and remains for the
    /// lifetime of the stream. No time will be allocated for compacting or garbage collection.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">Type to use for the array.</typeparam>
    [DebuggerDisplay("Start = {Start}; Length = {Length}; Free = {Free}")]
    public class CircularBuffer<T>
    {
        private readonly T[] m_Array;
        private int m_Start;
        private int m_Count;

        /// <summary>
        /// Allocate an Array of type T[] of particular capacity.
        /// </summary>
        /// <param name="capacity">Size of array to allocate.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> must be positive.</exception>
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
            m_Array = new T[capacity];
            m_Start = 0;
            m_Count = 0;
        }

        /// <summary>
        /// Circular buffer based on an already allocated array.
        /// </summary>
        /// <param name="array">Array (zero indexed) to allocate.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="array"/> must have at least one element;</exception>
        /// <remarks>
        /// The array is used as the storage for the circular buffer. No copy of the array is made. The initial index in
        /// the circular buffer is index 0 in the array. The array is assumed to be completely used (i.e. it is
        /// initialized with zero bytes Free).
        /// </remarks>
        public CircularBuffer(T[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length == 0) throw new ArgumentException("Array must have at least one element", nameof(array));
            m_Array = array;
            m_Start = 0;
            m_Count = array.Length;
        }

        /// <summary>
        /// Circular buffer based on an already allocated array.
        /// </summary>
        /// <param name="array">Array (zero indexed) to allocate.</param>
        /// <param name="count">Length of data in array, beginning from offset 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Initial <paramref name="count"/> must be within range of <paramref name="array"/>
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="array"/> must have at least one element;</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// The array is used as the storage for the circular buffer. No copy of the array is made, only a reference.
        /// The initial index in the array is 0. The value <paramref name="count"/> sets the initial length of the
        /// array. So an initial <paramref name="count"/> of zero would imply an empty circular buffer.
        /// </remarks>
        public CircularBuffer(T[] array, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length == 0) throw new ArgumentException("Array must have at least one element", nameof(array));
            if (count < 0 || count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be within range of the array");

            m_Array = array;
            m_Start = 0;
            m_Count = count;
        }

        /// <summary>
        /// Circular buffer based on an already allocated array.
        /// </summary>
        /// <param name="array">Array (zero indexed) to allocate.</param>
        /// <param name="offset">Offset of first byte in the array.</param>
        /// <param name="count">
        /// Length of data in <paramref name="array"/>, wrapping to the start of the <paramref name="array"/>.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="array"/> must have at least one element;</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> must be within range of <paramref name="array"/>;
        /// <para>- or -</para>
        /// <paramref name="offset"/> exceeds the <paramref name="array"/> boundaries.
        /// </exception>
        /// <remarks>
        /// The array is used as the storage for the circular buffer. No copy of the array is made, only a reference.
        /// The <paramref name="offset"/> is defined to be the first entry in the circular buffer. This may be any value
        /// from zero to the last index (<c>Array.Length - 1</c>). The value <paramref name="count"/> is the amount of
        /// data in the array, and it may cause wrapping (so that by setting offset near the end, a value of count may
        /// be set so that data can be considered at the end and beginning of the array given).
        /// </remarks>
        public CircularBuffer(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Length == 0) throw new ArgumentException("Array must have at least one element", nameof(array));
            if (count < 0 || count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "must be within range of the array");
            if (offset < 0 || offset >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "exceeds array boundaries");

            m_Array = array;
            m_Start = offset;
            m_Count = count;
        }

        /// <summary>
        /// Get start index into array where data begins.
        /// </summary>
        public int Start { get { return m_Start; } }

        /// <summary>
        /// Get end index into array where data ends.
        /// </summary>
        /// <remarks>
        /// This property is useful to know from what element in the underlying array that data can be written to.
        /// </remarks>
        public int End { get { return (m_Start + m_Count) % m_Array.Length; } }

        /// <summary>
        /// Get total length of data in array.
        /// </summary>
        /// <remarks>
        /// Returns the amount of allocated data in the circular buffer. The following rule applies:
        /// <see cref="Length"/> + <see cref="Free"/> = <see cref="Capacity"/>.
        /// </remarks>
        public int Length { get { return m_Count; } }

        /// <summary>
        /// Get total free data in array.
        /// </summary>
        /// <remarks>
        /// Returns the total amount of free elements in the circular buffer. The following rule applies:
        /// <see cref="Length"/> + <see cref="Free"/> = <see cref="Capacity"/>.
        /// </remarks>
        public int Free { get { return m_Array.Length - m_Count; } }

        /// <summary>
        /// Get the total capacity of the array.
        /// </summary>
        /// <remarks>
        /// Get the total number of elements allocated for the underlying array of the circular buffer. The following
        /// rule applies: <see cref="Length"/> + <see cref="Free"/> = <see cref="Capacity"/>.
        /// </remarks>
        public int Capacity { get { return m_Array.Length; } }

        /// <summary>
        /// Convert an index from the start of the data to read to an array index.
        /// </summary>
        /// <param name="index">
        /// Index in circular buffer, where an index of 0 is equivalent to the <see cref="Start"/> property.
        /// </param>
        /// <returns>Index in array that can be used in array based operations.</returns>
        public int ToArrayIndex(int index) { return (m_Start + index) % m_Array.Length; }

        /// <summary>
        /// Get length of continuous available space from the current position to the end of the array or until the
        /// buffer is full.
        /// </summary>
        /// <remarks>
        /// This function is useful if you need to pass the array to another function that will then fill the contents
        /// of the buffer. You would pass <see cref="End"/> as the offset for where writing the data should start, and
        /// <b>WriteLength</b> as the length of buffer space available until the end of the array buffer. After the read
        /// operation that writes in to your buffer, the array is completely full, or until the end of the array.
        /// <para>
        /// Such a property is necessary in case that the free space wraps around the buffer. Where below <c>X</c> is
        /// your stream you wish to read from, <c>b</c> is the circular buffer instantiated as the type
        /// <c>CircularBuffer{T}</c>.
        /// <code language="csharp">
        /// <![CDATA[
        /// c = X.Read(b.Array, b.End, b.WriteLength);
        /// b.Produce(c);
        /// ]]>
        /// </code>
        /// If the property <b>WriteLength</b> is not zero, then there is space in the buffer to read data.
        /// </para>
        /// </remarks>
        public int WriteLength
        {
            get
            {
                if (m_Start + m_Count >= m_Array.Length) return m_Array.Length - m_Count;
                return m_Array.Length - m_Start - m_Count;
            }
        }

        /// <summary>
        /// Get the length of the continuous amount of data that can be read in a single copy operation from the start
        /// of the buffer data.
        /// </summary>
        /// <remarks>
        /// This function is useful if you need to pass the array to another function that will use the contents of the
        /// array. You would pass <see cref="Start"/> as the offset for reading data and <see cref="ReadLength"/> as the
        /// count. Then based on the amount of data operated on, you would free space with
        /// <c><see cref="Consume"/>(ReadLength).</c>
        /// </remarks>
        public int ReadLength
        {
            get
            {
                if (m_Start + m_Count >= m_Array.Length) return m_Array.Length - m_Start;
                return m_Count;
            }
        }

        /// <summary>
        /// Given an offset, calculate the length of data that can be read until the end of the block.
        /// </summary>
        /// <param name="offset">The offset into the circular buffer to test for the read length.</param>
        /// <returns>Length of the block that can be read from <paramref name="offset"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="offset"/> may not be negative.</exception>
        /// <remarks>
        /// Similar to the property <c>ReadLength</c>, this function takes an argument <c>offset</c> which is used to
        /// determine the length of data that can be read from that offset, until either the end of the block, or the
        /// end of the buffer.
        /// <para>
        /// This function is useful if you want to read a block of data, not starting from the offset 0 (and you don't
        /// want to consume the data before hand to reach an offset of zero).
        /// </para>
        /// <para>
        /// The example below, will calculate a checksum from the third byte in the block for the length of data. If the
        /// block to read from offset 3 can be done in one operation, it will do so. Else it must be done in two
        /// operations, first from offset 3 to the end, then from offset 0 for the remaining data.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code language="csharp">
        /// <![CDATA[
        /// short crc;
        /// if (buffer.GetReadBlock(3) >= length - 3) {
        ///   crc = crc16.Compute(buffer.Array, buffer.ToArrayIndex(3), length - 3);
        /// } else {
        ///   crc = crc16.Compute(buffer.Array, buffer.ToArrayIndex(3), buffer.ReadLength - 3);
        ///   crc = crc16.Compute(crc, buffer.Array, 0, length - buffer.ReadLength);
        /// }
        /// ]]>
        /// </code>
        /// </example>
        public int GetReadBlock(int offset)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "may not be negative");
            if (offset >= m_Count) return 0;

            int s = (m_Start + offset) % m_Array.Length;
            int c = m_Count - offset;

            if (s + c >= m_Array.Length) return m_Array.Length - s;
            return c;
        }

        /// <summary>
        /// Consume array elements (freeing space from the beginning) updating pointers in the circular buffer.
        /// </summary>
        /// <param name="length">Amount of data to consume.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="length"/> is negative, or cannot consume more data than exists.
        /// </exception>
        /// <remarks>
        /// This method advances the internal pointers for <i>Start</i> based on the <i>length</i> that should be
        /// consumed. The pointer <i>End</i> does not change. It is important that this method does not <i>Reset()</i>
        /// the buffer in case that all data is consumed. A common scenario with Streams is to write into the buffer
        /// using asynchronous I/O. If a <i>Reset()</i> occurs during an asynchronous I/O <i>ReadFile()</i>, the
        /// <i>End</i> pointer is also changed, so that when a <i>Produce()</i> occurs on completion of the
        /// <i>ReadFile()</i> operation, the pointers are updated, but not using the pointers before the <i>Reset()</i>.
        /// No crash would occur (so long as the underlying array is pinned), but data corruption would occur if this
        /// method were not used in this particular scenario.
        /// </remarks>
        public void Consume(int length)
        {
            if (length < 0 || length > m_Count)
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot consume negative length, or more data than exists");

            // Note, some implementations may rely on the pointers being correctly advanced also in
            // the case that data is consumed.
            m_Count -= length;
            m_Start = (m_Start + length) % m_Array.Length;
        }

        /// <summary>
        /// Produce bytes (allocating space at the end) updating pointers in the circular buffer.
        /// </summary>
        /// <param name="length">
        /// The number of bytes to indicate that have been added from the index <see cref="End"/> to the end of the
        /// array and possibly again from the start of the array if overlapped.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Cannot produce negative <paramref name="length"/>, or producing <paramref name="length"/> exceeds
        /// <see cref="Free"/>.
        /// </exception>
        public void Produce(int length)
        {
            if (length < 0 || length > m_Array.Length - m_Count)
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot produce negative length, or exceed buffer free");

            m_Count += length;
        }

        /// <summary>
        /// Revert elements produced to the end of the circular buffer.
        /// </summary>
        /// <param name="length">
        /// The number of bytes to remove from the end of the array, moving the <see cref="End"/> property to the left,
        /// leaving the <see cref="Start"/> property untouched.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="length"/> must be positive and not exceed the number of elements in the circular buffer.
        /// </exception>
        /// <remarks>
        /// This method can be used to remove data that has been added to the end of the circular buffer. When using
        /// this data structure for streams, you would not use this property to ensure consistency of your stream (your
        /// <c>Read</c> operation would consume from your circular buffer and <c>Write</c> would produce data to your
        /// circular buffer.
        /// </remarks>
        public void Revert(int length)
        {
            if (length < 0 || length >= m_Count)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "must be positive and not exceed the number of elements in the circular buffer");

            m_Count -= length;
        }

        /// <summary>
        /// Reset the pointers in the circular buffer, effectively noting the circular buffer as empty.
        /// </summary>
        public void Reset()
        {
            m_Count = 0;
            m_Start = 0;
        }

        /// <summary>
        /// Get the reference to the array that's allocated.
        /// </summary>
        /// <remarks>
        /// This property allows you to access the content of the data in the circular buffer in an efficient manner.
        /// You can then use this property along with <see cref="Start"/>, <see cref="ReadLength"/>, <see cref="End"/>
        /// and <see cref="WriteLength"/> for knowing where in the buffer to read and write.
        /// </remarks>
        public T[] Array { get { return m_Array; } }

        /// <summary>
        /// Access an element in the array using the Start as index 0.
        /// </summary>
        /// <param name="index">Index into the array referenced from <see cref="Start"/>.</param>
        /// <returns>Contents of the array.</returns>
        public T this[int index]
        {
            get
            {
#if DEBUG
                if (index >= Length) throw new ArgumentOutOfRangeException(nameof(index), "Index exceeded Buffer Length");
#endif
                return m_Array[(m_Start + index) % m_Array.Length];
            }
            set
            {
#if DEBUG
                if (index >= Length) throw new ArgumentOutOfRangeException(nameof(index), "Index exceeded Buffer Length");
#endif
                m_Array[(m_Start + index) % m_Array.Length] = value;
            }
        }

        /// <summary>
        /// Copy data from array to the end of this circular buffer and update the length.
        /// </summary>
        /// <param name="array">Array to copy from.</param>
        /// <returns>Number of bytes copied.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from the <c>buffer</c> array that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(T[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            return Append(array, 0, array.Length);
        }

        /// <summary>
        /// Copy data from array to the end of this circular buffer and update the length.
        /// </summary>
        /// <param name="array">Array to copy from.</param>
        /// <param name="offset">Offset to copy data from.</param>
        /// <param name="count">Length of data to copy.</param>
        /// <returns>Number of bytes copied.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> or <paramref name="count"/> may not be negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="offset"/> and <paramref name="count"/> exceed <paramref name="array"/> boundaries.
        /// </exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from <paramref name="array"/> that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "may not be negative");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "may not be negative");
            if (offset > array.Length - count) throw new ArgumentException("Parameters exceed array boundary");
            if (m_Count == Capacity) return 0;
            if (count == 0) return 0;

            if (count <= WriteLength) {
                System.Array.Copy(array, offset, m_Array, End, count);
            } else {
                count = Math.Min(Free, count);
                System.Array.Copy(array, offset, m_Array, End, WriteLength);
                System.Array.Copy(array, offset + WriteLength, m_Array, 0, count - WriteLength);
            }
            Produce(count);
            return count;
        }

#if NETSTANDARD
        /// <summary>
        /// Copy data from array to the end of this circular buffer and update the length.
        /// </summary>
        /// <param name="array">Array to copy from.</param>
        /// <returns>Number of bytes copied.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from <paramref name="array"/> that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(ReadOnlySpan<T> array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            int length = array.Length;
            if (length <= WriteLength) {
                array.CopyTo(m_Array.AsSpan(End, length));
            } else {
                length = Math.Min(Free, length);
                int block2 = length - WriteLength;
                array[0..WriteLength].CopyTo(m_Array.AsSpan(End, WriteLength));
                array[WriteLength..length].CopyTo(m_Array.AsSpan(0, block2));
            }
            Produce(length);
            return length;
        }
#endif

        /// <summary>
        /// Copy data from the circular buffer to the end of this circular buffer.
        /// </summary>
        /// <param name="buffer">Buffer to append.</param>
        /// <returns>Amount of data appended.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="buffer"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from the <c>buffer</c> array that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(CircularBuffer<T> buffer)
        {
            return Append(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Copy data from the circular buffer to the end of this circular buffer.
        /// </summary>
        /// <param name="buffer">Buffer to append.</param>
        /// <param name="count">Number of bytes to append.</param>
        /// <returns>Amount of data appended.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="buffer"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="count"/> would exceed boundaries of <paramref name="buffer"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> may not be negative.</exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from the <c>buffer</c> array that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(CircularBuffer<T> buffer, int count)
        {
            return Append(buffer, 0, count);
        }

        /// <summary>
        /// Copy data from the circular buffer to the end of this circular buffer.
        /// </summary>
        /// <param name="buffer">Buffer to append.</param>
        /// <param name="count">Number of bytes to append.</param>
        /// <param name="offset">Offset into the buffer to start appending.</param>
        /// <returns>Amount of data appended.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="buffer"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="count"/> may not be negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="offset"/> and <paramref name="count"/> would exceed boundaries of <paramref name="buffer"/>.
        /// </exception>
        /// <remarks>
        /// Data is copied to the end of the Circular Buffer. The amount of data that could be copied is dependent on
        /// the amount of free space. The result is the number of elements from the <c>buffer</c> array that is copied
        /// into the Circular Buffer. Pointers in the circular buffer are updated appropriately.
        /// </remarks>
        public int Append(CircularBuffer<T> buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "may not be negative");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "may not be negative");
            if (offset > buffer.Length - count) throw new ArgumentException("Parameters exceed buffer boundary");
            if (m_Count == Capacity) return 0;
            if (count == 0) return 0;

            int o = (buffer.Start + offset) % buffer.Capacity;
            int c = Math.Min(Free, count);
            int r = c;

            while (r > 0) {
                int rl = (o + r >= buffer.Capacity) ? (buffer.Capacity - o) : r;
                int cp = Math.Min(r, WriteLength);
                cp = Math.Min(cp, rl);
                System.Array.Copy(buffer.Array, o, m_Array, End, cp);
                Produce(cp);
                r -= cp;
                o = (o + cp) % buffer.Capacity;
            }
            return c;
        }

        /// <summary>
        /// Append a single element to the end of the Circular Buffer.
        /// </summary>
        /// <param name="element">The element to add at the end of the buffer.</param>
        /// <returns>Amount of data appended. 1 if successful, 0 if no space available.</returns>
        public int Append(T element)
        {
            if (m_Count == Capacity) return 0;

            m_Array[End] = element;
            Produce(1);
            return 1;
        }

        /// <summary>
        /// Retrieve a single element from the Circular buffer and consume it.
        /// </summary>
        /// <returns>The value at index 0.</returns>
        /// <exception cref="InvalidOperationException">Circular buffer is empty.</exception>
        public T Pop()
        {
            if (m_Count == 0) throw new InvalidOperationException("Circular Buffer is empty");
            T result = m_Array[m_Start];
            Consume(1);
            return result;
        }

        /// <summary>
        /// Copy data from the circular buffer to the array and then consume the data from the circular buffer.
        /// </summary>
        /// <param name="array">The array to copy the data to.</param>
        /// <returns>The number of bytes that were moved.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>Data is copied to the first element in the array, up to the length of the array.</remarks>
        public int MoveTo(T[] array)
        {
            int l = CopyTo(array);
            Consume(l);
            return l;
        }

        /// <summary>
        /// Copy data from the circular buffer to the array and then consume the data from the circular buffer.
        /// </summary>
        /// <param name="array">The array to copy the data to.</param>
        /// <param name="offset">Offset into the array to copy to.</param>
        /// <param name="count">Amount of data to copy to.</param>
        /// <returns>The number of bytes that were moved.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="offset"/> may not be negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="offset"/> and <paramref name="count"/> would exceed <paramref name="array"/> length.
        /// </exception>
        /// <remarks>
        /// This method is very similar to the <see cref="CopyTo(T[], int, int)"/> method, but it will also consume the
        /// data that was copied.
        /// </remarks>
        public int MoveTo(T[] array, int offset, int count)
        {
            int l = CopyTo(array, offset, count);
            Consume(l);
            return l;
        }

#if NETSTANDARD
        /// <summary>
        /// Copy data from the circular buffer to the span array and then consume the data from the circular buffer.
        /// </summary>
        /// <param name="array">The span array to copy the data to.</param>
        /// <returns>The number of bytes that were moved.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method is very similar to the <see cref="CopyTo(T[], int, int)"/> method, but it will also consume the
        /// data that was copied.
        /// </remarks>
        public int MoveTo(Span<T> array)
        {
            int l = CopyTo(array);
            Consume(l);
            return l;
        }
#endif

        /// <summary>
        /// Copy data from the circular buffer to the array.
        /// </summary>
        /// <param name="array">The array to copy the data to.</param>
        /// <returns>The number of bytes that were copied.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="array"/> may not be <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Data is copied from the first element in the array, up to the length of the array. The data from the
        /// Circular Buffer is <i>not</i> consumed. You must do this yourself. Else use the MoveTo() method.
        /// </remarks>
        public int CopyTo(T[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            return CopyTo(array, 0, array.Length);
        }

        /// <summary>
        /// Copy data from the circular buffer to the array.
        /// </summary>
        /// <param name="array">The array to copy the data to.</param>
        /// <param name="offset">Offset into the array to copy to.</param>
        /// <param name="count">Amount of data to copy to.</param>
        /// <returns>The number of bytes that were copied.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> is negative;
        /// <para>- or -</para>
        /// <paramref name="offset"/> is negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="offset"/> and <paramref name="count"/> would exceed <paramref name="array"/> length.
        /// </exception>
        /// <remarks>
        /// Data is copied from the circular buffer into the array specified, at the offset given. The data from the
        /// Circular Buffer is <i>not</i> consumed. You must do this yourself. Else use the MoveTo() method.
        /// </remarks>
        public int CopyTo(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (count == 0) return 0;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "may not be negative");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "may not be negative");
            if (offset > array.Length - count) throw new ArgumentException("Offset and count exceed boundary length");

            if (count <= ReadLength) {
                // The block of data is one continuous block to copy
                System.Array.Copy(m_Array, Start, array, offset, count);
            } else {
                count = Math.Min(Length, count);
                System.Array.Copy(m_Array, Start, array, offset, ReadLength);
                System.Array.Copy(m_Array, 0, array, offset + ReadLength, count - ReadLength);
            }
            return count;
        }

#if NETSTANDARD
        /// <summary>
        /// Copy data from the circular buffer to the span array.
        /// </summary>
        /// <param name="array">The span array to copy the data to.</param>
        /// <returns>The number of bytes that were copied.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Data is copied from the circular buffer into the array specified, at the offset given. The data from the
        /// Circular Buffer is <i>not</i> consumed. You must do this yourself. Else use the MoveTo() method.
        /// </remarks>
        public int CopyTo(Span<T> array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            int length = array.Length;
            if (length <= ReadLength) {
                // The block of data is one continuous block to copy
                m_Array.AsSpan(Start, length).CopyTo(array);
            } else {
                length = Math.Min(Length, length);
                m_Array.AsSpan(Start, ReadLength).CopyTo(array);
                m_Array.AsSpan(0, length - ReadLength).CopyTo(array[ReadLength..]);
            }
            return length;
        }
#endif

        /// <summary>
        /// Searches for a specific element in the array.
        /// </summary>
        /// <param name="element">The element to search for.</param>
        /// <returns>The location in the buffer where the first element is found.
        /// If the element could not be found, -1 is returned.</returns>
        public int Substring(T element)
        {
            return Substring(element, 0);
        }

        /// <summary>
        /// Searches for a specific element in the array from the offset provided.
        /// </summary>
        /// <param name="element">The element to search for.</param>
        /// <param name="offset">The offset into the buffer.</param>
        /// <returns>The location in the buffer where the first element is found from <paramref name="offset"/>.
        /// If the element could not be found, -1 is returned.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> must be in buffer range of <see cref="Length"/>.
        /// </exception>
        public int Substring(T element, int offset)
        {
            if (offset < 0 || offset > m_Count) throw new ArgumentOutOfRangeException(nameof(offset), "out of range [0..Length-1]");

            int count = m_Count - offset;
            int start = (m_Start + offset) % m_Array.Length;
            int rl = (start + count >= m_Array.Length) ? m_Array.Length - start : count;

            for (int i = start; i < start + rl; i++) {
                if (m_Array[i].Equals(element)) return (i >= m_Start) ? i - m_Start : (m_Array.Length - m_Start) + i;
            }
            count -= rl;
            if (count > 0) {
                for (int i = 0; i < count; i++) {
                    if (m_Array[i].Equals(element)) return offset + rl + i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Searches for specific elements in the array.
        /// </summary>
        /// <param name="elements">The elements to search for.</param>
        /// <returns>The location in the buffer where the first element is found.
        /// If the element could not be found, -1 is returned.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="elements"/> may not be <see langword="null"/>.
        /// </exception>
        public int Substring(T[] elements)
        {
            return Substring(elements, 0);
        }

        /// <summary>
        /// Searches for a specific element in the array from the offset provided.
        /// </summary>
        /// <param name="elements">The elements to search for.</param>
        /// <param name="offset">The offset into the buffer.</param>
        /// <returns>The location in the buffer where the first element is found.
        /// If the element could not be found, -1 is returned.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> must be in buffer range <see cref="Length"/>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="elements"/> may not be <see langword="null"/>.
        /// </exception>
        public int Substring(T[] elements, int offset)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            if (offset < 0 || offset > m_Count) throw new ArgumentOutOfRangeException(nameof(offset), "out of range [0..Length-1]");

            int count = m_Count - offset;
            int start = (m_Start + offset) % m_Array.Length;
            int rl = (start + count >= m_Array.Length) ? m_Array.Length - start : count;
            int jm = elements.Length;

            for (int i = start; i < start + rl; i++) {
                for (int j = 0; j < jm; j++) {
                    if (m_Array[i].Equals(elements[j])) return (i >= m_Start) ? i - m_Start : (m_Array.Length - m_Start) + i;
                }
            }
            count -= rl;
            if (count > 0) {
                for (int i = 0; i < count; i++) {
                    for (int j = 0; j < jm; j++) {
                        if (m_Array[i].Equals(elements[j])) return offset + rl + i;
                    }
                }
            }
            return -1;
        }
    }
}
