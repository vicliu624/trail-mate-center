namespace RJCP.IO.Buffer
{
    using System;
    using System.Text;

    /// <summary>
    /// A set of useful extensions to the CircularBuffer for specific data types.
    /// </summary>
    public static class CircularBufferExtensions
    {
        /// <summary>
        /// Convert the contents of the circular buffer into a string.
        /// </summary>
        /// <param name="buff">The circular buffer based on char.</param>
        /// <returns>A string containing the contents of the circular buffer.</returns>
        /// <remarks>This method will not consume the data in the CircularBuffer{char}.</remarks>
        public static string GetString(this CircularBuffer<char> buff)
        {
            if (buff == null) return null;
            return buff.GetString(buff.Length);
        }

        /// <summary>
        /// Convert the contents of the circular buffer into a string.
        /// </summary>
        /// <param name="buff">The circular buffer based on char.</param>
        /// <param name="length">Number of characters to convert to a string.</param>
        /// <returns>A string of up to length characters.</returns>
        /// <remarks>This method will not consume the data in the CircularBuffer{char}.</remarks>
        public static string GetString(this CircularBuffer<char> buff, int length)
        {
            if (buff == null) return null;
            if (length == 0) return string.Empty;
            if (length > buff.Length) length = buff.Length;
            if (buff.Start + length > buff.Capacity) {
                StringBuilder sb = new StringBuilder(length);
                sb.Append(buff.Array, buff.Start, buff.Capacity - buff.Start);
                sb.Append(buff.Array, 0, length + buff.Start - buff.Capacity);
                return sb.ToString();
            }
            return new string(buff.Array, buff.Start, length);
        }

        /// <summary>
        /// Convert the contents of the circular buffer into a string.
        /// </summary>
        /// <param name="buff">The circular buffer based on char.</param>
        /// <param name="offset">The offset into the circular buffer.</param>
        /// <param name="length">Number of characters to convert to a string.</param>
        /// <returns>
        /// A string of up to length characters, from the circular buffer starting at the offset specified..
        /// </returns>
        /// <remarks>This method will not consume the data in the CircularBuffer{char}.</remarks>
        public static string GetString(this CircularBuffer<char> buff, int offset, int length)
        {
            if (buff == null) return null;
            if (length == 0) return string.Empty;
            if (offset > buff.Length) return string.Empty;
            if (offset + length > buff.Length) length = buff.Length - offset;

            int start = (buff.Start + offset) % buff.Capacity;
            if (start + length > buff.Capacity) {
                StringBuilder sb = new StringBuilder(length);
                sb.Append(buff.Array, start, buff.Capacity - start);
                sb.Append(buff.Array, 0, length + start - buff.Capacity);
                return sb.ToString();
            } else {
                return new string(buff.Array, start, length);
            }
        }

        /// <summary>
        /// Use a decoder to convert from a Circular Buffer of bytes into a char array.
        /// </summary>
        /// <param name="decoder">The decoder to do the conversion.</param>
        /// <param name="bytes">The circular buffer of bytes to convert from.</param>
        /// <param name="chars">An array to store the converted characters.</param>
        /// <param name="charIndex">The first element of <i>chars</i> in which data is stored.</param>
        /// <param name="charCount">Maximum number of characters to write.</param>
        /// <param name="flush">
        /// <see langword="true"/> to indicate that no further data is to be converted; otherwise,
        /// <see langword="false"/>.
        /// </param>
        /// <param name="bytesUsed">
        /// When this method returns, contains the number of bytes that were used in the conversion. This parameter is
        /// passed uninitialized.
        /// </param>
        /// <param name="charsUsed">
        /// When this method returns, contains the number of characters from chars that were produced by the conversion.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <param name="completed">
        /// When this method returns, contains <see langword="true"/> if all the characters specified by byteCount were
        /// converted; otherwise, <see langword="false"/>. This parameter is passed uninitialized.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The output buffer <paramref name="chars"/> is too small to contain any of the converted input.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bytes"/> or <paramref name="chars"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="charIndex"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="charCount"/> may not be negative.
        /// </exception>
        /// <remarks>
        /// This method should behave the same as the decoder for an array of bytes of equal size.
        /// <para>
        /// The <i>completed</i> output parameter indicates whether all the data in the input buffer was converted and
        /// stored in the output buffer. This parameter is set to <see langword="false"/> if the number of bytes
        /// specified by the <i>bytes.Length</i> parameter cannot be converted without exceeding the number of
        /// characters specified by the charCount parameter.
        /// </para>
        /// <para>
        /// The completed parameter can also be set to <see langword="false"/>, even though the all bytes were consumed.
        /// This situation occurs if there is still data in the Decoder object that has not been stored in the bytes
        /// buffer.
        /// </para>
        /// <para>
        /// There are a few noted deviations from using the Decoder on an array of bytes, instead of a Circular Buffer.
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// When converting a sequence of bytes to multiple chars, if those sequences result in the minimum number of
        /// characters being written as 2 or more characters, slight discrepancies occur. A UTF8 decoder would convert
        /// the sequence F3 A0 82 84 to the two characters DB40 DC84. The UTF8 decoder would not consume any of the 4
        /// bytes if all 4 bytes are immediately available to a single call to the Decoder.Convert() function and
        /// instead raise an exception. This Convert() function may consume some of these bytes and indicate success, if
        /// the byte sequence wraps over from the end of the array to the beginning of the array. The number of bytes
        /// consumed (bytesUsed) is correct and characters produced (charsUsed) is also correct. There is no error found
        /// according to the MS documentation. The next call will result in an exception instead. So this function may:
        /// consume more bytes than expected (but with the correct results); and may not raise an exception immediately
        /// if those bytes were consumed.
        /// </item>
        /// </list>
        /// </remarks>
        public static void Convert(this Decoder decoder, CircularBuffer<byte> bytes, char[] chars, int charIndex, int charCount, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (chars == null) throw new ArgumentNullException(nameof(chars));
            if (charIndex < 0) throw new ArgumentOutOfRangeException(nameof(charIndex), "may not be negative");
            if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount), "may not be negative");
            if (chars.Length - charIndex < charCount) throw new ArgumentException("charIndex and charCount exceed char buffer boundaries");

            bytesUsed = 0;
            charsUsed = 0;
            completed = true;
            bool outFlush = false;

            int rl = bytes.ReadLength;
            while (rl > 0 && charCount > 0) {
                int bu;
                int cu;
                if (rl == bytes.Length) outFlush = flush;
                try {
                    decoder.Convert(bytes.Array, bytes.Start, rl,
                        chars, charIndex, charCount,
                        outFlush, out bu, out cu, out completed);
                } catch (ArgumentException e) {
                    if (e.ParamName == null || !e.ParamName.Equals("chars")) throw;

                    // NOTE: While a decoder may not consume anything, using the CircularBuffer extension may, if the
                    // bytes need to be passed to the decoder twice. This is because we can't know what bytes may cause
                    // the error. The same kind of behavior would occur if you feed one byte at a time to the decoder
                    // yourself. It will be passed twice if the byte sequence is split between the end and the start of
                    // the circular queue.

                    if (bytesUsed == 0) throw;
                    completed = false;
                    return;
                }
                bytes.Consume(bu);
                bytesUsed += bu;
                charCount -= cu;
                charsUsed += cu;
                charIndex += cu;
                rl = bytes.ReadLength;
            }
        }

        /// <summary>
        /// Use a decoder to convert from a Circular Buffer of bytes into a Circular Buffer of chars.
        /// </summary>
        /// <param name="decoder">The decoder to do the conversion.</param>
        /// <param name="bytes">The circular buffer of bytes to convert from.</param>
        /// <param name="chars">The circular buffer of chars to convert to.</param>
        /// <param name="charCount">Maximum number of characters to write.</param>
        /// <param name="flush">
        /// <see langword="true"/> to indicate that no further data is to be converted; otherwise,
        /// <see langword="false"/>.
        /// </param>
        /// <param name="bytesUsed">
        /// When this method returns, contains the number of bytes that were used in the conversion. This parameter is
        /// passed uninitialized.
        /// </param>
        /// <param name="charsUsed">
        /// When this method returns, contains the number of characters from chars that were produced by the conversion.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <param name="completed">
        /// When this method returns, contains <see langword="true"/> if all the characters specified by byteCount were
        /// converted; otherwise, <see langword="false"/>. This parameter is passed uninitialized.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The output buffer <paramref name="chars"/> is too small to contain any of the converted input.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bytes"/> or <paramref name="chars"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="charCount"/> may not be negative.</exception>
        public static void Convert(this Decoder decoder, CircularBuffer<byte> bytes, CircularBuffer<char> chars, int charCount, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (chars == null) throw new ArgumentNullException(nameof(chars));

            charCount = Math.Min(chars.Free, charCount);
            bytesUsed = 0;
            charsUsed = 0;
            completed = true;
            bool outFlush = false;

            int rl = bytes.ReadLength;
            while (rl > 0 && charCount > 0) {
                int bu;
                int cu;
                if (rl == bytes.Length) outFlush = flush;
                try {
                    decoder.Convert(bytes.Array, bytes.Start, rl,
                        chars.Array, chars.End, Math.Min(chars.WriteLength, charCount),
                        outFlush, out bu, out cu, out completed);
                    bytes.Consume(bu);
                    chars.Produce(cu);
                    rl = bytes.ReadLength;
                } catch (ArgumentException e) {
                    if (e.ParamName == null || !e.ParamName.Equals("chars")) throw;

                    // Decoder tried to write bytes, but not enough free space. We need to write to a temp array, then
                    // copy into the circular buffer. We assume that the underlying decoder hasn't changed state.
                    if (charCount <= chars.WriteLength) {
                        // There's no free space left, so we raise the same exception as the decoder
                        if (bytesUsed == 0) throw;
                        completed = false;
                        return;
                    }

                    int tmpLen = Math.Min(16, charCount);
                    char[] tmp = new char[tmpLen];
                    try {
                        decoder.Convert(bytes.Array, bytes.Start, rl,
                            tmp, 0, tmp.Length, outFlush, out bu, out cu, out completed);
                    } catch (ArgumentException e2) {
                        if (e2.ParamName == null || !e2.ParamName.Equals("chars")) throw;
                        if (bytesUsed == 0) throw;
                        completed = false;
                        return;
                    }
                    bytes.Consume(bu);
                    chars.Append(tmp, 0, cu);
                    rl = bytes.ReadLength;
                }
                bytesUsed += bu;
                charCount -= cu;
                charsUsed += cu;
            }
        }

        /// <summary>
        /// Use a decoder to convert from a Circular Buffer of bytes into a Circular Buffer of chars.
        /// </summary>
        /// <param name="decoder">The decoder to do the conversion.</param>
        /// <param name="bytes">The circular buffer of bytes to convert from.</param>
        /// <param name="chars">The circular buffer of chars to convert to.</param>
        /// <param name="flush">
        /// <see langword="true"/> to indicate that no further data is to be converted; otherwise,
        /// <see langword="false"/>.
        /// </param>
        /// <param name="bytesUsed">
        /// When this method returns, contains the number of bytes that were used in the conversion. This parameter is
        /// passed uninitialized.
        /// </param>
        /// <param name="charsUsed">
        /// When this method returns, contains the number of characters from chars that were produced by the conversion.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <param name="completed">
        /// When this method returns, contains <see langword="true"/> if all the characters specified by byteCount were
        /// converted; otherwise, <see langword="false"/>. This parameter is passed uninitialized.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The output buffer <paramref name="chars"/> is too small to contain any of the converted input.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bytes"/> or <paramref name="chars"/> may not be <see langword="null"/>.
        /// </exception>
        public static void Convert(this Decoder decoder, CircularBuffer<byte> bytes, CircularBuffer<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (chars == null) throw new ArgumentNullException(nameof(chars));
            decoder.Convert(bytes, chars, chars.Free, flush, out bytesUsed, out charsUsed, out completed);
        }

        /// <summary>
        /// Use a decoder to convert from an array of bytes into a char CircularBuffer.
        /// </summary>
        /// <param name="decoder">The decoder to do the conversion.</param>
        /// <param name="bytes">The array of bytes to convert.</param>
        /// <param name="byteIndex">Start index in bytes array.</param>
        /// <param name="byteCount">Number of bytes to convert in the byte array.</param>
        /// <param name="chars">The circular buffer of chars to convert to.</param>
        /// <param name="flush">
        /// <see langword="true"/> to indicate that no further data is to be converted; otherwise,
        /// <see langword="false"/>.
        /// </param>
        /// <param name="bytesUsed">
        /// When this method returns, contains the number of bytes that were used in the conversion. This parameter is
        /// passed uninitialized.
        /// </param>
        /// <param name="charsUsed">
        /// When this method returns, contains the number of characters from chars that were produced by the conversion.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <param name="completed">
        /// When this method returns, contains <see langword="true"/> if all the characters specified by byteCount were
        /// converted; otherwise, <see langword="false"/>. This parameter is passed uninitialized.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The output buffer <paramref name="bytes"/> is too small to contain any of the converted input.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bytes"/> or <paramref name="chars"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="byteIndex"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="byteCount"/> may not be negative.
        /// </exception>
        public static void Convert(this Decoder decoder, byte[] bytes, int byteIndex, int byteCount, CircularBuffer<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (chars == null) throw new ArgumentNullException(nameof(chars));
            if (byteIndex < 0) throw new ArgumentOutOfRangeException(nameof(byteIndex), "may not be negative");
            if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount), "may not be negative");
            if (bytes.Length - byteIndex < byteCount) throw new ArgumentException("byteIndex and byteCount exceed byte buffer boundaries");

            bytesUsed = 0;
            charsUsed = 0;
            completed = true;
            if (byteCount == 0) return;

            do {
                int bu;
                int cu;
                try {
                    if (bytesUsed != 0 && chars.WriteLength == 0) {
                        completed = false;
                        return;
                    }
                    decoder.Convert(bytes, byteIndex, byteCount,
                        chars.Array, chars.End, chars.WriteLength,
                        flush, out bu, out cu, out completed);
                    byteCount -= bu;
                    bytesUsed += bu;
                    byteIndex += bu;
                    chars.Produce(cu);
                    charsUsed += cu;
                } catch (ArgumentException e) {
                    if (e.ParamName == null || !e.ParamName.Equals("chars")) throw;

                    // Decoder tried to write bytes, but not enough free space. We need to write to a temp array, then
                    // copy into the circular buffer. We assume that the underlying decoder hasn't changed state.
                    if (chars.WriteLength == chars.Free) {
                        // There's no free space left, so we raise the same exception as the decoder
                        if (bytesUsed == 0) throw;
                        completed = false;
                        return;
                    }

                    int tempLen = Math.Min(16, chars.Free);
                    char[] tmp = new char[tempLen];
                    try {
                        decoder.Convert(bytes, byteIndex, byteCount,
                            tmp, 0, tmp.Length, flush, out bu, out cu, out completed);
                    } catch (ArgumentException e2) {
                        // There still isn't enough space, so abort
                        if (e2.ParamName == null || !e2.ParamName.Equals("chars")) throw;
                        if (bytesUsed == 0) throw;
                        completed = false;
                        return;
                    }
                    byteCount -= bu;
                    bytesUsed += bu;
                    byteIndex += bu;
                    chars.Append(tmp, 0, cu);
                    charsUsed += cu;
                }
            } while (!completed);
        }

        /// <summary>
        /// Converts an array of Unicode characters to a byte sequence storing the result in a circular buffer.
        /// </summary>
        /// <param name="encoder">The encoder to use for the conversion.</param>
        /// <param name="chars">An array of characters to convert.</param>
        /// <param name="charIndex">The first element of <i>chars</i> to convert.</param>
        /// <param name="charCount">The number of elements of <i>chars</i> to convert.</param>
        /// <param name="bytes">Circular buffer where converted bytes are stored.</param>
        /// <param name="flush">
        /// <see langword="true"/> to indicate no further data is to be converted; otherwise, <see langword="false"/>
        /// </param>
        /// <param name="charsUsed">
        /// When this method returns, contains the number of characters from chars that were produced by the conversion.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <param name="bytesUsed">
        /// When this method returns, contains the number of bytes that were used in the conversion. This parameter is
        /// passed uninitialized.
        /// </param>
        /// <param name="completed">
        /// When this method returns, contains <see langword="true"/> if all the characters specified by byteCount were
        /// converted; otherwise, <see langword="false"/>. This parameter is passed uninitialized.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The output buffer <paramref name="chars"/> is too small to contain any of the converted input.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bytes"/> or <paramref name="chars"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="charIndex"/> may not be negative;
        /// <para>- or -</para>
        /// <paramref name="charCount"/> may not be negative.
        /// </exception>
        public static void Convert(this Encoder encoder, char[] chars, int charIndex, int charCount, CircularBuffer<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            // The code here is the same as the "Decoder" version as they do the same thing. Unfortunately, .NET doesn't
            // have a base class for this, so we need two separate encoder/decoder methods.

            if (chars == null) throw new ArgumentNullException(nameof(chars));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (charIndex < 0) throw new ArgumentOutOfRangeException(nameof(charIndex), "may not be negative");
            if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount), "may not be negative");
            if (chars.Length - charIndex < charCount) throw new ArgumentException("charIndex and charCount exceed char buffer boundaries");

            bytesUsed = 0;
            charsUsed = 0;
            completed = true;
            if (charCount == 0) return;

            do {
                int bu;
                int cu;
                try {
                    if (charsUsed != 0 && bytes.WriteLength == 0) {
                        completed = false;
                        return;
                    }

                    encoder.Convert(chars, charIndex, charCount,
                        bytes.Array, bytes.End, bytes.WriteLength,
                        flush, out cu, out bu, out completed);
                    charCount -= cu;
                    charsUsed += cu;
                    charIndex += cu;
                    bytes.Produce(bu);
                    bytesUsed += bu;
                } catch (ArgumentException e) {
                    if (e.ParamName == null || !e.ParamName.Equals("bytes")) throw;

                    // Encoder tried to write chars, but not enough free space. We need to write to a temp array, then
                    // copy into the circular buffer. We assume that the underlying encoder hasn't changed state.
                    if (bytes.WriteLength == bytes.Free) {
                        // There's no free space left, so we raise the same exception as the decoder
                        if (charsUsed == 0) throw;
                        completed = false;
                        return;
                    }

                    int tempLen = Math.Min(16, bytes.Free);
                    byte[] tmp = new byte[tempLen];
                    try {
                        encoder.Convert(chars, charIndex, charCount,
                            tmp, 0, tmp.Length, flush, out cu, out bu, out completed);
                    } catch (ArgumentException e2) {
                        // There still isn't enough space, so abort
                        if (e2.ParamName == null || !e2.ParamName.Equals("bytes")) throw;
                        if (charsUsed == 0) throw;
                        completed = false;
                        return;
                    }
                    charCount -= cu;
                    charsUsed += cu;
                    charIndex += cu;
                    bytes.Append(tmp, 0, bu);
                    bytesUsed += bu;
                }
            } while (!completed);
        }
    }
}
