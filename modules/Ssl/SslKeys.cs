using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// PEM_read_bio_PrivateKey for SSL_CTX_use_PrivateKey_file: the first private key block of a
    /// file (PKCS#8, encrypted PKCS#8, traditional RSA/EC with or without the legacy Proc-Type
    /// encryption), with the password callback asked only for an encrypted one; and the
    /// X509Certificate2 SslStream needs for a certificate and its key.
    /// </summary>
    internal static class SslKeys
    {
        internal const int PEM_BUFSIZE = 1024;

        /// <summary>
        /// The key, or null with the error queued (PEM lib for a missing key or a wrong password).
        /// password(size) is _password_callback: the password bytes, or it throws.
        /// </summary>
        internal static AsymmetricAlgorithm ReadPrivateKey(byte[] data, Func<int, byte[]> password)
        {
            int pos = 0;
            Pem.Block block;
            try
            {
                while ((block = Pem.Next(data, ref pos)) != null && !Pem.IsPrivateKeyLabel(block.Label))
                {
                }
            }
            catch (Asn1Exception)
            {
                block = null;
            }
            if (block == null)
            {
                ErrQueue.Raise(SslErr.PemLib(SslErr.LIB_SSL));
                return null;
            }
            try
            {
                switch (block.Label)
                {
                    case "PRIVATE KEY":
                        return ImportPkcs8(block.Der, null);
                    case "ENCRYPTED PRIVATE KEY":
                        {
                            var pass = password?.Invoke(PEM_BUFSIZE);
                            if (pass == null)
                                break;
                            return ImportPkcs8(block.Der, pass);
                        }
                    case "RSA PRIVATE KEY":
                    case "EC PRIVATE KEY":
                        {
                            var der = block.Der;
                            if (block.Headers.TryGetValue("Proc-Type", out var proc) && proc.Contains("ENCRYPTED"))
                            {
                                var pass = password?.Invoke(PEM_BUFSIZE);
                                if (pass == null)
                                    break;
                                der = LegacyDecrypt(der, block.Headers.GetValueOrDefault("DEK-Info"), pass);
                                if (der == null)
                                    break;
                            }
                            if (block.Label == "RSA PRIVATE KEY")
                            {
                                var rsa = RSA.Create();
                                rsa.ImportRSAPrivateKey(der, out _);
                                return rsa;
                            }
                            var ec = ECDsa.Create();
                            ec.ImportECPrivateKey(der, out _);
                            return ec;
                        }
                }
            }
            catch (CryptographicException)
            {
            }
            ErrQueue.Raise(SslErr.PemLib(SslErr.LIB_SSL));
            return null;
        }

        private static AsymmetricAlgorithm ImportPkcs8(byte[] der, byte[] pass)
        {
            Exception last = null;
            foreach (var make in new Func<AsymmetricAlgorithm>[] { RSA.Create, ECDsa.Create, DSA.Create })
            {
                var key = make();
                try
                {
                    if (pass == null)
                    {
                        switch (key)
                        {
                            case RSA r: r.ImportPkcs8PrivateKey(der, out _); break;
                            case ECDsa e: e.ImportPkcs8PrivateKey(der, out _); break;
                            case DSA d: d.ImportPkcs8PrivateKey(der, out _); break;
                        }
                    }
                    else
                    {
                        switch (key)
                        {
                            case RSA r: r.ImportEncryptedPkcs8PrivateKey(pass, der, out _); break;
                            case ECDsa e: e.ImportEncryptedPkcs8PrivateKey(pass, der, out _); break;
                            case DSA d: d.ImportEncryptedPkcs8PrivateKey(pass, der, out _); break;
                        }
                    }
                    return key;
                }
                catch (Exception error) when (error is CryptographicException or PlatformNotSupportedException)
                {
                    last = error;
                    key.Dispose();
                }
            }
            throw last as CryptographicException ?? new CryptographicException();
        }

        /// <summary>PEM_do_header: the traditional "Proc-Type: 4,ENCRYPTED" body (EVP_BytesToKey with MD5).</summary>
        private static byte[] LegacyDecrypt(byte[] data, string dekInfo, byte[] pass)
        {
            if (dekInfo == null)
                return null;
            var parts = dekInfo.Split(',');
            if (parts.Length != 2)
                return null;
            byte[] iv = Convert.FromHexString(parts[1].Trim());
            string cipher = parts[0].Trim().ToUpperInvariant();
            SymmetricAlgorithm alg;
            int keyLength;
            switch (cipher)
            {
                case "DES-EDE3-CBC": alg = TripleDES.Create(); keyLength = 24; break;
                case "DES-CBC": alg = DES.Create(); keyLength = 8; break;
                case "AES-128-CBC": alg = Aes.Create(); keyLength = 16; break;
                case "AES-192-CBC": alg = Aes.Create(); keyLength = 24; break;
                case "AES-256-CBC": alg = Aes.Create(); keyLength = 32; break;
                default: return null;
            }
            // EVP_BytesToKey(cipher, MD5, salt = iv[0..8], pass, count 1)
            var key = new byte[keyLength];
            byte[] prev = Array.Empty<byte>();
            int filled = 0;
            var salt = iv.AsSpan(0, 8).ToArray();
            while (filled < keyLength)
            {
                prev = MD5.HashData(prev.Concat(pass).Concat(salt).ToArray());
                int n = Math.Min(prev.Length, keyLength - filled);
                Array.Copy(prev, 0, key, filled, n);
                filled += n;
            }
            using (alg)
            {
                alg.Key = key;
                return alg.DecryptCbc(data, iv, PaddingMode.PKCS7);
            }
        }

        /// <summary>The key's SubjectPublicKeyInfo, for X509_check_private_key.</summary>
        internal static byte[] PublicKeyInfo(AsymmetricAlgorithm key) => key switch
        {
            RSA r => r.ExportSubjectPublicKeyInfo(),
            ECDsa e => e.ExportSubjectPublicKeyInfo(),
            DSA d => d.ExportSubjectPublicKeyInfo(),
            _ => null,
        };

        /// <summary>X509_check_private_key: the certificate's public key is the key's.</summary>
        internal static bool Matches(X509Cert cert, AsymmetricAlgorithm key)
        {
            var spki = PublicKeyInfo(key);
            if (spki == null)
                return false;
            if (spki.AsSpan().SequenceEqual(cert.SubjectPublicKeyInfo))
                return true;
            // Compare the parameters rather than the encodings (an EC key may name its curve differently).
            try
            {
                switch (key)
                {
                    case RSA r:
                        {
                            using var other = RSA.Create();
                            other.ImportSubjectPublicKeyInfo(cert.SubjectPublicKeyInfo, out _);
                            var a = r.ExportParameters(false);
                            var b = other.ExportParameters(false);
                            return a.Modulus.AsSpan().SequenceEqual(b.Modulus) && a.Exponent.AsSpan().SequenceEqual(b.Exponent);
                        }
                    case ECDsa e:
                        {
                            using var other = ECDsa.Create();
                            other.ImportSubjectPublicKeyInfo(cert.SubjectPublicKeyInfo, out _);
                            var a = e.ExportParameters(false);
                            var b = other.ExportParameters(false);
                            return a.Q.X.AsSpan().SequenceEqual(b.Q.X) && a.Q.Y.AsSpan().SequenceEqual(b.Q.Y);
                        }
                }
            }
            catch (CryptographicException)
            {
            }
            return false;
        }

        /// <summary>
        /// The certificate with its private key as SslStream takes it. SChannel cannot use an
        /// ephemeral (in-memory) key for a server, so on Windows the pair goes through PKCS#12.
        /// </summary>
        internal static X509Certificate2 WithKey(X509Cert cert, AsymmetricAlgorithm key)
        {
            var plain = new X509Certificate2(cert.Der);
            X509Certificate2 joined = key switch
            {
                RSA r => plain.CopyWithPrivateKey(r),
                ECDsa e => plain.CopyWithPrivateKey(e),
                DSA d => plain.CopyWithPrivateKey(d),
                _ => throw new CryptographicException("unsupported key"),
            };
            if (!OperatingSystem.IsWindows())
                return joined;
            var pfx = joined.Export(X509ContentType.Pkcs12);
            return new X509Certificate2(pfx, (string)null, X509KeyStorageFlags.Exportable);
        }
    }
}
