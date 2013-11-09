//
// WindowsSecureMimeContext.cs
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
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509.Store;

using MimeKit.IO;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Asn1.X509;

namespace MimeKit.Cryptography {
	/// <summary>
	/// An S/MIME cryptography context that uses Windows' <see cref="System.Security.Cryptography.X509Certificates.X509Store"/>.
	/// </summary>
	public class WindowsSecureMimeContext : SecureMimeContext
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.Cryptography.WindowsSecureMimeContext"/> class.
		/// </summary>
		/// <param name="store">The X509 certificate store.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="store"/> is <c>null</c>.
		/// </exception>
		public WindowsSecureMimeContext (X509Store store)
		{
			if (store == null)
				throw new ArgumentNullException ("store");

			CertificateStore = store;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.Cryptography.WindowsSecureMimeContext"/> class.
		/// </summary>
		public WindowsSecureMimeContext ()
		{
			CertificateStore = new X509Store (StoreName.My, StoreLocation.CurrentUser);
			CertificateStore.Open (OpenFlags.ReadWrite);
		}

		/// <summary>
		/// Gets or sets the X509 certificate store.
		/// </summary>
		/// <value>The X509 certificate store.</value>
		public X509Store CertificateStore {
			get; protected set;
		}

		#region implemented abstract members of SecureMimeContext

		/// <summary>
		/// Gets the X.509 certificate based on the selector.
		/// </summary>
		/// <returns>The certificate on success; otherwise <c>null</c>.</returns>
		/// <param name="selector">The search criteria for the certificate.</param>
		protected override Org.BouncyCastle.X509.X509Certificate GetCertificate (IX509Selector selector)
		{
			foreach (var certificate in CertificateStore.Certificates) {
				var cert = DotNetUtilities.FromX509Certificate (certificate);

				if (selector.Match (cert))
					return cert;
			}

			return null;
		}

		/// <summary>
		/// Gets the private key based on the provided selector.
		/// </summary>
		/// <returns>The private key on success; otherwise <c>null</c>.</returns>
		/// <param name="selector">The search criteria for the private key.</param>
		protected override AsymmetricKeyParameter GetPrivateKey (IX509Selector selector)
		{
			foreach (var certificate in CertificateStore.Certificates) {
				if (!certificate.HasPrivateKey)
					continue;

				var cert = DotNetUtilities.FromX509Certificate (certificate);

				if (selector.Match (cert)) {
					var pair = DotNetUtilities.GetKeyPair (certificate.PrivateKey);
					return pair.Private;
				}
			}

			return null;
		}

		/// <summary>
		/// Gets the trusted anchors.
		/// </summary>
		/// <returns>The trusted anchors.</returns>
		protected override Org.BouncyCastle.Utilities.Collections.HashSet GetTrustedAnchors ()
		{
			var anchors = new Org.BouncyCastle.Utilities.Collections.HashSet ();
			var root = new X509Store (StoreName.Root, StoreLocation.CurrentUser);

			try {
				root.Open (OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
			} catch {
				return anchors;
			}

			foreach (var certificate in root.Certificates) {
				var cert = DotNetUtilities.FromX509Certificate (certificate);
				anchors.Add (new TrustAnchor (cert, null));
			}

			root.Close ();

			return anchors;
		}

		/// <summary>
		/// Gets the intermediate certificates.
		/// </summary>
		/// <returns>The intermediate certificates.</returns>
		protected override IX509Store GetIntermediateCertificates ()
		{
			var store = new X509CertificateStore ();

			foreach (var certificate in CertificateStore.Certificates) {
				var cert = DotNetUtilities.FromX509Certificate (certificate);
				store.Add (cert);
			}

			return store;
		}

		/// <summary>
		/// Gets the certificate revocation lists.
		/// </summary>
		/// <returns>The certificate revocation lists.</returns>
		protected override IX509Store GetCertificateRevocationLists ()
		{
			var crls = new List<X509Crl> ();

			return X509StoreFactory.Create ("Crl/Collection", new X509CollectionStoreParameters (crls));
		}

		/// <summary>
		/// Gets the X509 certificate associated with the <see cref="MimeKit.MailboxAddress"/>.
		/// </summary>
		/// <returns>The certificate.</returns>
		/// <param name="mailbox">The mailbox.</param>
		/// <exception cref="CertificateNotFoundException">
		/// A certificate for the specified <paramref name="mailbox"/> could not be found.
		/// </exception>
		protected override CmsRecipient GetCmsRecipient (MailboxAddress mailbox)
		{
			var certificates = CertificateStore.Certificates;//.Find (X509FindType.FindByKeyUsage, flags, true);

			foreach (var certificate in certificates) {
				if (certificate.GetNameInfo (X509NameType.EmailName, false) != mailbox.Address)
					continue;

				var cert = DotNetUtilities.FromX509Certificate (certificate);

				return new CmsRecipient (cert);
			}

			throw new CertificateNotFoundException (mailbox, "A valid certificate could not be found.");
		}

		/// <summary>
		/// Gets the cms signer for the specified <see cref="MimeKit.MailboxAddress"/>.
		/// </summary>
		/// <returns>The cms signer.</returns>
		/// <param name="mailbox">The mailbox.</param>
		/// <param name="digestAlgo">The preferred digest algorithm.</param>
		/// <exception cref="CertificateNotFoundException">
		/// A certificate for the specified <paramref name="mailbox"/> could not be found.
		/// </exception>
		protected override CmsSigner GetCmsSigner (MailboxAddress mailbox, DigestAlgorithm digestAlgo)
		{
			var certificates = CertificateStore.Certificates;//.Find (X509FindType.FindByKeyUsage, flags, true);

			foreach (var certificate in certificates) {
				if (certificate.GetNameInfo (X509NameType.EmailName, false) != mailbox.Address)
					continue;

				if (!certificate.HasPrivateKey)
					continue;

				var pair = DotNetUtilities.GetKeyPair (certificate.PrivateKey);
				var cert = DotNetUtilities.FromX509Certificate (certificate);
				var signer = new CmsSigner (cert, pair.Private);
				signer.DigestAlgorithm = digestAlgo;

				return signer;
			}

			throw new CertificateNotFoundException (mailbox, "A valid signing certificate could not be found.");
		}

		/// <summary>
		/// Imports certificates and keys from a pkcs12-encoded stream.
		/// </summary>
		/// <param name="stream">The raw certificate and key data.</param>
		/// <param name="password">The password to unlock the stream.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="stream"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// Importing keys is not supported by this cryptography context.
		/// </exception>
		public override void Import (Stream stream, string password)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");

			if (password == null)
				throw new ArgumentNullException ("password");

			byte[] rawData;

			if (stream is MemoryBlockStream) {
				rawData = ((MemoryBlockStream) stream).ToArray ();
			} else if (stream is MemoryStream) {
				rawData = ((MemoryStream) stream).ToArray ();
			} else {
				using (var memory = new MemoryStream ()) {
					stream.CopyTo (memory, 4096);
					rawData = memory.ToArray ();
				}
			}

			var certs = new X509Certificate2Collection ();
			certs.Import (rawData, password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

			CertificateStore.AddRange (certs);
		}

		#endregion

		#region implemented abstract members of CryptographyContext

		/// <summary>
		/// Imports certificates (as from a certs-only application/pkcs-mime part)
		/// from the specified stream.
		/// </summary>
		/// <param name="stream">The raw certificate data.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="stream"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.Security.Cryptography.CryptographicException">
		/// An error occurred while importing.
		/// </exception>
		public override void Import (Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");

			byte[] rawData;

			if (stream is MemoryBlockStream) {
				rawData = ((MemoryBlockStream) stream).ToArray ();
			} else if (stream is MemoryStream) {
				rawData = ((MemoryStream) stream).ToArray ();
			} else {
				using (var memory = new MemoryStream  ()) {
					stream.CopyTo (memory, 4096);
					rawData = memory.ToArray ();
				}
			}

			var contentInfo = new ContentInfo (rawData);
			var signed = new SignedCms (contentInfo, false);

			CertificateStore.AddRange (signed.Certificates);
		}

		#endregion

		/// <summary>
		/// Dispose the specified disposing.
		/// </summary>
		/// <param name="disposing">If set to <c>true</c> disposing.</param>
		protected override void Dispose (bool disposing)
		{
			if (disposing && CertificateStore != null) {
				CertificateStore.Close ();
				CertificateStore = null;
			}

			base.Dispose (disposing);
		}
	}
}
