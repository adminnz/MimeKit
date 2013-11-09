//
// MeasuringStream.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013 Jeffrey Stedfast
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;

namespace MimeKit.IO {
	/// <summary>
	/// A stream useful for measuring the amount of data written.
	/// </summary>
	public class MeasuringStream : Stream
	{
		bool disposed;
		long position;
		long length;

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.IO.MeasuringStream"/> class.
		/// </summary>
		public MeasuringStream ()
		{
		}

		void CheckDisposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("stream");
		}

		#region implemented abstract members of Stream

		/// <summary>
		/// Checks whether or not the stream supports reading.
		/// </summary>
		/// <value><c>true</c> if the stream supports reading; otherwise, <c>false</c>.</value>
		public override bool CanRead {
			get { return false; }
		}

		/// <summary>
		/// Checks whether or not the stream supports writing.
		/// </summary>
		/// <value>
		/// <c>true</c> if the stream supports writing; otherwise, <c>false</c>.</value>
		public override bool CanWrite {
			get { return true; }
		}

		/// <summary>
		/// Checks whether or not the stream supports seeking.
		/// </summary>
		/// <value><c>true</c> if the stream supports seeking; otherwise, <c>false</c>.</value>
		public override bool CanSeek {
			get { return true; }
		}

		/// <summary>
		/// Checks whether or not reading and writing to the stream can timeout.
		/// </summary>
		/// <value><c>true</c> if reading and writing to the stream can timeout; otherwise, <c>false</c>.</value>
		public override bool CanTimeout {
			get { return false; }
		}

		/// <summary>
		/// Gets the length of the stream.
		/// </summary>
		/// <value>The length of the stream.</value>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		public override long Length {
			get {
				CheckDisposed ();

				return length;
			}
		}

		/// <summary>
		/// Gets or sets the position of the stream.
		/// </summary>
		/// <value>The position of the stream.</value>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support seeking.
		/// </exception>
		public override long Position {
			get { return position; }
			set { Seek (value, SeekOrigin.Begin); }
		}

		void ValidateArguments (byte[] buffer, int offset, int count)
		{
			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (offset < 0 || offset > buffer.Length)
				throw new ArgumentOutOfRangeException ("offset");

			if (count < 0 || offset + count > buffer.Length)
				throw new ArgumentOutOfRangeException ("count");
		}

		/// <summary>
		/// Reads data into the specified buffer.
		/// </summary>
		/// <returns>The number of bytes read.</returns>
		/// <param name='buffer'>The buffer to read data into.</param>
		/// <param name='offset'>The offset into the buffer to start reading data.</param>
		/// <param name='count'>The number of bytes to read.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		public override int Read (byte[] buffer, int offset, int count)
		{
			CheckDisposed ();

			throw new NotSupportedException ("The stream does not support reading");
		}

		/// <summary>
		/// Writes the specified buffer.
		/// </summary>
		/// <param name='buffer'>The buffer to write.</param>
		/// <param name='offset'>The offset of the first byte to write.</param>
		/// <param name='count'>The number of bytes to write.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support writing.
		/// </exception>
		public override void Write (byte[] buffer, int offset, int count)
		{
			CheckDisposed ();

			ValidateArguments (buffer, offset, count);

			position += count;

			length = Math.Max (length, position);
		}

		/// <summary>
		/// Seeks to the specified offset.
		/// </summary>
		/// <param name='offset'>The offset from the specified origin.</param>
		/// <param name='origin'>The origin from which to seek.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="origin"/> is not a valid <see cref="System.IO.SeekOrigin"/>. 
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		public override long Seek (long offset, SeekOrigin origin)
		{
			long real;

			CheckDisposed ();

			switch (origin) {
			case SeekOrigin.Begin:
				real = offset;
				break;
			case SeekOrigin.Current:
				real = position + offset;
				break;
			case SeekOrigin.End:
				real = length + offset;
				break;
			default:
				throw new ArgumentOutOfRangeException ("origin", "Invalid SeekOrigin specified");
			}

			// sanity check the resultant offset
			if (real < 0)
				throw new IOException ("Cannot seek to a position before the beginning of the stream");

			// short-cut if we are seeking to our current position
			if (real == position)
				return position;

			if (real > length)
				throw new IOException ("Cannot seek beyond the end of the stream");

			position = real;

			return position;
		}

		/// <summary>
		/// Flushes any internal output buffers.
		/// </summary>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support writing.
		/// </exception>
		public override void Flush ()
		{
			CheckDisposed ();

			// nothing to do...
		}

		/// <summary>
		/// Sets the length.
		/// </summary>
		/// <param name='value'>The new length.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="value"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		public override void SetLength (long value)
		{
			CheckDisposed ();

			if (value < 0)
				throw new ArgumentOutOfRangeException ("value");

			position = Math.Min (position, value);
			length = value;
		}

		#endregion

		/// <summary>
		/// Dispose the specified disposing.
		/// </summary>
		/// <param name="disposing">If set to <c>true</c> disposing.</param>
		protected override void Dispose (bool disposing)
		{
			base.Dispose (disposing);
			disposed = true;
		}
	}
}
