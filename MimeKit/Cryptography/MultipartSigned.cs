//
// MultipartSigned.cs
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
using System.Collections.Generic;

using MimeKit.IO;
using MimeKit.IO.Filters;

namespace MimeKit.Cryptography {
	/// <summary>
	/// A signed multipart, as sued by both S/MIME and PGP/MIME protocols.
	/// </summary>
	public class MultipartSigned : Multipart
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.Cryptography.MultipartSigned"/> class.
		/// </summary>
		/// <remarks>This constructor is used by <see cref="MimeKit.MimeParser"/>.</remarks>
		/// <param name="entity">Information used by the constructor.</param>
		public MultipartSigned (MimeEntityConstructorInfo entity) : base (entity)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.Cryptography.MultipartSigned"/> class.
		/// </summary>
		public MultipartSigned () : base ("signed")
		{
		}

		static void PrepareEntityForSigning (MimeEntity entity)
		{
			if (entity is Multipart) {
				// Note: we do not want to modify multipart/signed parts
				if (entity is MultipartSigned)
					return;

				var multipart = (Multipart) entity;

				foreach (var subpart in multipart)
					PrepareEntityForSigning (subpart);
			} else if (entity is MessagePart) {
				var mpart = (MessagePart) entity;

				if (mpart.Message != null && mpart.Message.Body != null)
					PrepareEntityForSigning (mpart.Message.Body);
			} else {
				var part = (MimePart) entity;

				if (part.ContentTransferEncoding == ContentEncoding.Binary)
					part.ContentTransferEncoding = ContentEncoding.Base64;
				else if (part.ContentTransferEncoding != ContentEncoding.Base64)
					part.ContentTransferEncoding = ContentEncoding.QuotedPrintable;
			}
		}

		/// <summary>
		/// Creates a new <see cref="MimeKit.Cryptography.MultipartSigned"/> instance with the entity as the content.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.Cryptography.MultipartSigned"/> instance.</returns>
		/// <param name="ctx">The cryptography context to use for signing.</param>
		/// <param name="signer">The signer.</param>
		/// <param name="digestAlgo">The digest algorithm to use for signing.</param>
		/// <param name="entity">The entity to sign.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="ctx"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="entity"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="CertificateNotFoundException">
		/// A signing certificate could not be found for <paramref name="signer"/>.
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Cms.CmsException">
		/// An error occurred in the cryptographic message syntax subsystem.
		/// </exception>
		public static MultipartSigned Create (CryptographyContext ctx, MailboxAddress signer, DigestAlgorithm digestAlgo, MimeEntity entity)
		{
			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (entity == null)
				throw new ArgumentNullException ("entity");

			PrepareEntityForSigning (entity);

			using (var memory = new MemoryStream ()) {
				using (var filtered = new FilteredStream (memory)) {
					// Note: see rfc3156, section 3 - second note
					filtered.Add (new ArmoredFromFilter ());

					// Note: see rfc3156, section 5.4 (this is the main difference between rfc2015 and rfc3156)
					filtered.Add (new TrailingWhitespaceFilter ());

					// Note: see rfc2015 or rfc3156, section 5.1
					filtered.Add (new Unix2DosFilter ());

					entity.WriteTo (filtered);
					filtered.Flush ();
				}

				memory.Position = 0;

				// Note: we need to parse the modified entity structure to preserve any modifications
				var parser = new MimeParser (memory, MimeFormat.Entity);
				var parsed = parser.ParseEntity ();
				memory.Position = 0;

				// sign the cleartext content
				var signature = ctx.Sign (signer, digestAlgo, memory);
				var micalg = ctx.GetMicAlgorithmName (digestAlgo);
				var signed = new MultipartSigned ();

				// set the protocol and micalg Content-Type parameters
				signed.ContentType.Parameters["protocol"] = ctx.SignatureProtocol;
				signed.ContentType.Parameters["micalg"] = micalg;

				// add the modified/parsed entity as our first part
				signed.Add (parsed);

				// add the detached signature as the second part
				signed.Add (signature);

				return signed;
			}
		}

		/// <summary>
		/// Creates a new <see cref="MimeKit.Cryptography.MultipartSigned"/> instance with the entity as the content.
		/// </summary>
		/// <param name="ctx">The S/MIME context to use for signing.</param>
		/// <param name="signer">The signer.</param>
		/// <param name="entity">The entity to sign.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="ctx"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="entity"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Cms.CmsException">
		/// An error occurred in the cryptographic message syntax subsystem.
		/// </exception>
		public static MultipartSigned Create (SecureMimeContext ctx, CmsSigner signer, MimeEntity entity)
		{
			if (ctx == null)
				throw new ArgumentNullException ("ctx");

			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (entity == null)
				throw new ArgumentNullException ("entity");

			PrepareEntityForSigning (entity);

			using (var memory = new MemoryStream ()) {
				using (var filtered = new FilteredStream (memory)) {
					// Note: see rfc3156, section 3 - second note
					filtered.Add (new ArmoredFromFilter ());

					// Note: see rfc3156, section 5.4 (this is the main difference between rfc2015 and rfc3156)
					filtered.Add (new TrailingWhitespaceFilter ());

					// Note: see rfc2015 or rfc3156, section 5.1
					filtered.Add (new Unix2DosFilter ());

					entity.WriteTo (filtered);
					filtered.Flush ();
				}

				memory.Position = 0;

				// Note: we need to parse the modified entity structure to preserve any modifications
				var parser = new MimeParser (memory, MimeFormat.Entity);
				var parsed = parser.ParseEntity ();
				memory.Position = 0;

				// sign the cleartext content
				var micalg = ctx.GetMicAlgorithmName (signer.DigestAlgorithm);
				var signature = ctx.Sign (signer, memory);
				var signed = new MultipartSigned ();

				// set the protocol and micalg Content-Type parameters
				signed.ContentType.Parameters["protocol"] = ctx.SignatureProtocol;
				signed.ContentType.Parameters["micalg"] = micalg;

				// add the modified/parsed entity as our first part
				signed.Add (parsed);

				// add the detached signature as the second part
				signed.Add (signature);

				return signed;
			}
		}

		/// <summary>
		/// Creates a new <see cref="MimeKit.Cryptography.MultipartSigned"/> instance with the entity as the content.
		/// </summary>
		/// <param name="signer">The signer.</param>
		/// <param name="entity">The entity to sign.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="entity"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// A cryptography context suitable for signing could not be found.
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Cms.CmsException">
		/// An error occurred in the cryptographic message syntax subsystem.
		/// </exception>
		public static MultipartSigned Create (CmsSigner signer, MimeEntity entity)
		{
			using (var ctx = (SecureMimeContext) CryptographyContext.Create ("application/pkcs7-signature")) {
				return Create (ctx, signer, entity);
			}
		}

		/// <summary>
		/// Verify the multipart/signed content.
		/// </summary>
		/// <returns>A signer info collection.</returns>
		/// <param name="ctx">The cryptography context to use for verifying the signature.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="ctx"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.FormatException">
		/// The multipart is malformed in some way.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <paramref name="ctx"/> does not support verifying the signature part.
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Cms.CmsException">
		/// An error occurred in the cryptographic message syntax subsystem.
		/// </exception>
		public IList<IDigitalSignature> Verify (CryptographyContext ctx)
		{
			if (ctx == null)
				throw new ArgumentNullException ("ctx");

			var protocol = ContentType.Parameters["protocol"];
			if (string.IsNullOrEmpty (protocol))
				throw new FormatException ("The multipart/signed part did not specify a protocol.");

			if (!ctx.Supports (protocol.Trim ()))
				throw new NotSupportedException ("The specified cryptography context does not support the signature protocol.");

			if (Count < 2)
				throw new FormatException ("The multipart/signed part did not contain the expected children.");

			var signature = this[1] as MimePart;
			if (signature == null || signature.ContentObject == null)
				throw new FormatException ("The signature part could not be found.");

			var ctype = signature.ContentType;
			var value = string.Format ("{0}/{1}", ctype.MediaType, ctype.MediaSubtype);
			if (!ctx.Supports (value))
				throw new NotSupportedException (string.Format ("The specified cryptography context does not support '{0}'.", value));

			using (var signatureData = new MemoryStream ()) {
				signature.ContentObject.DecodeTo (signatureData);
				signatureData.Position = 0;

				using (var cleartext = new MemoryStream ()) {
					// Note: see rfc2015 or rfc3156, section 5.1
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;

					this[0].WriteTo (options, cleartext);
					cleartext.Position = 0;

					return ctx.Verify (cleartext, signatureData);
				}
			}
		}

		/// <summary>
		/// Verify the multipart/signed content.
		/// </summary>
		/// <returns>A signer info collection.</returns>
		/// <exception cref="System.FormatException">
		/// <para>The <c>protocol</c> parameter was not specified.</para>
		/// <para>-or-</para>
		/// <para>The multipart is malformed in some way.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// A suitable <see cref="MimeKit.Cryptography.CryptographyContext"/> for
		/// verifying could not be found.
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Cms.CmsException">
		/// An error occurred in the cryptographic message syntax subsystem.
		/// </exception>
		public IList<IDigitalSignature> Verify ()
		{
			var protocol = ContentType.Parameters["protocol"];
			if (string.IsNullOrEmpty (protocol))
				throw new FormatException ("The multipart/signed part did not specify a protocol.");

			protocol = protocol.Trim ().ToLowerInvariant ();

			using (var ctx = CryptographyContext.Create (protocol)) {
				return Verify (ctx);
			}
		}
	}
}
