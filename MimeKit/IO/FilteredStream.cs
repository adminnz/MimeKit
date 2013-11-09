﻿//
// FilteredStream.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2012 Jeffrey Stedfast
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
using System.Collections.Generic;

using MimeKit.IO.Filters;

namespace MimeKit.IO {
	/// <summary>
	/// A stream which filters data as it is read or written.
	/// </summary>
	public class FilteredStream : Stream
	{
		const int ReadBufferSize = 4096;

		enum IOOperation : byte {
			Read,
			Write
		}

		List<IMimeFilter> filters = new List<IMimeFilter> ();
		IOOperation lastOp = IOOperation.Read;
		int filteredLength;
		int filteredIndex;
		byte[] filtered;
		byte[] readbuf;
		long position;
		bool disposed;
		bool flushed;

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.IO.FilteredStream"/> class.
		/// </summary>
		/// <param name='source'>The underlying stream to filter.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="source"/> is <c>null</c>.
		/// </exception>
		public FilteredStream (Stream source)
		{
			if (source == null)
				throw new ArgumentNullException ("source");

			position = source.Position;
			Source = source;
		}

		/// <summary>
		/// Gets the underlying source stream.
		/// </summary>
		/// <value>The underlying source stream.</value>
		public Stream Source {
			get; private set;
		}

		/// <summary>
		/// Adds the specified filter.
		/// </summary>
		/// <param name='filter'>The filter.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="filter"/> is <c>null</c>.
		/// </exception>
		public void Add (IMimeFilter filter)
		{
			if (filter == null)
				throw new ArgumentNullException ("filter");

			filters.Add (filter);
		}

		/// <summary>
		/// Contains the specified filter.
		/// </summary>
		/// <param name='filter'>The filter.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="filter"/> is <c>null</c>.
		/// </exception>
		public bool Contains (IMimeFilter filter)
		{
			if (filter == null)
				throw new ArgumentNullException ("filter");

			return filters.Contains (filter);
		}

		/// <summary>
		/// Remove the specified filter.
		/// </summary>
		/// <param name='filter'>The filter.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="filter"/> is <c>null</c>.
		/// </exception>
		public bool Remove (IMimeFilter filter)
		{
			if (filter == null)
				throw new ArgumentNullException ("filter");

			return filters.Remove (filter);
		}

		void CheckDisposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("FilteredStream");
		}

		void CheckCanRead ()
		{
			if (!Source.CanRead)
				throw new NotSupportedException ("The stream does not support reading");
		}

		void CheckCanWrite ()
		{
			if (!Source.CanWrite)
				throw new NotSupportedException ("The stream does not support writing");
		}

		#region Stream implementation

		/// <summary>
		/// Checks whether or not the stream supports reading.
		/// </summary>
		/// <value><c>true</c> if the stream supports reading; otherwise, <c>false</c>.</value>
		public override bool CanRead {
			get { return Source.CanRead; }
		}

		/// <summary>
		/// Checks whether or not the stream supports writing.
		/// </summary>
		/// <value><c>true</c> if the stream supports writing; otherwise, <c>false</c>.</value>
		public override bool CanWrite {
			get { return Source.CanWrite; }
		}

		/// <summary>
		/// Checks whether or not the stream supports seeking.
		/// </summary>
		/// <value><c>true</c> if the stream supports seeking; otherwise, <c>false</c>.</value>
		public override bool CanSeek {
			get { return false; }
		}

		/// <summary>
		/// Checks whether or not I/O operations can timeout.
		/// </summary>
		/// <value><c>true</c> if I/O operations can timeout; otherwise, <c>false</c>.</value>
		public override bool CanTimeout {
			get { return Source.CanTimeout; }
		}

		/// <summary>
		/// Gets the length of the stream.
		/// </summary>
		/// <value>The length of the stream.</value>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support setting the length.
		/// </exception>
		public override long Length {
			get { throw new NotSupportedException ("Cannot get the length of the stream"); }
		}

		/// <summary>
		/// Gets or sets the position of the stream.
		/// </summary>
		/// <value>The position of the stream.</value>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support seeking.
		/// </exception>
		public override long Position {
			get { return position; }
			set { throw new NotSupportedException ("The stream does not support seeking"); }
		}

		/// <summary>
		/// Gets or sets the read timeout.
		/// </summary>
		/// <value>The read timeout.</value>
		public override int ReadTimeout
		{
			get { return Source.ReadTimeout; }
			set { Source.ReadTimeout = value; }
		}

		/// <summary>
		/// Gets or sets the write timeout.
		/// </summary>
		/// <value>The write timeout.</value>
		public override int WriteTimeout
		{
			get { return Source.WriteTimeout; }
			set { Source.WriteTimeout = value; }
		}

		static void ValidateArguments (byte[] buffer, int offset, int count)
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
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support reading.
		/// </exception>
		public override int Read (byte[] buffer, int offset, int count)
		{
			CheckDisposed ();
			CheckCanRead ();

			ValidateArguments (buffer, offset, count);

			lastOp = IOOperation.Read;
			if (readbuf == null)
				readbuf = new byte[ReadBufferSize];

			int nread;

			if (filteredLength == 0) {
				if ((nread = Source.Read (readbuf, 0, ReadBufferSize)) <= 0)
					return nread;
				
				// filter the data we've just read...
				filteredLength = nread;
				filteredIndex = 0;
				filtered = readbuf;

				foreach (var filter in filters)
					filtered = filter.Filter (filtered, filteredIndex, filteredLength, out filteredIndex, out filteredLength);
			}

			// copy our filtered data into our caller's buffer
			nread = Math.Min (filteredLength, count);

			if (nread > 0) {
				Array.Copy (filtered, filteredIndex, buffer, offset, nread);
				filteredLength -= nread;
				filteredIndex += nread;
				position += nread;
			}

			return nread;
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
			CheckCanWrite ();

			ValidateArguments (buffer, offset, count);

			lastOp = IOOperation.Write;
			flushed = false;

			filteredIndex = offset;
			filteredLength = count;
			filtered = buffer;

			foreach (var filter in filters)
				filtered = filter.Filter (filtered, filteredIndex, filteredLength, out filteredIndex, out filteredLength);

			Source.Write (filtered, filteredIndex, filteredLength);
			position += count;
		}

		/// <summary>
		/// Seeks to the specified offset.
		/// </summary>
		/// <param name='offset'>The offset from the specified origin.</param>
		/// <param name='origin'>The origin from which to seek.</param>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support seeking.
		/// </exception>
		public override long Seek (long offset, SeekOrigin origin)
		{
			throw new NotSupportedException ("The stream does not support seeking");
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
			CheckCanWrite ();

			if (lastOp == IOOperation.Read)
				return;

			if (!flushed) {
				filtered = new byte[0];
				filteredIndex = 0;
				filteredLength = 0;

				foreach (var filter in filters)
					filtered = filter.Flush (filtered, filteredIndex, filteredLength, out filteredIndex, out filteredLength);

				flushed = true;
			}

			if (filteredLength > 0) {
				Source.Write (filtered, filteredIndex, filteredLength);
				filteredIndex = 0;
				filteredLength = 0;
			}

			Source.Flush ();
		}

		/// <summary>
		/// Sets the length.
		/// </summary>
		/// <param name='value'>The new length.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The stream has been disposed.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The stream does not support setting the length.
		/// </exception>
		public override void SetLength (long value)
		{
			CheckDisposed ();

			throw new NotSupportedException ("Cannot set a length on the stream");
		}

		/// <summary>
		/// Dispose the specified disposing.
		/// </summary>
		/// <param name="disposing">If set to <c>true</c> disposing.</param>
		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				if (filters != null) {
					filters.Clear ();
					filters = null;
				}

				readbuf = null;
			}

			base.Dispose (disposing);
			disposed = true;
		}

		#endregion
	}
}
