
        private const bool encryption  = true;  // always true for production use
        private const bool compression = true;  // always true for production use

        /*
         * Data for encryption.
         */
#ifdef SECRET_STUFF
        private static int rsaBits = 1280;
#endif
        private static int aesBits = 256;

        private static byte[] version = new ASCIIEncoding ().GetBytes 
               ("XMRBinary(" + ScriptCodeGen.COMPILED_VERSION_VALUE + ".2)");

#include "MMRScriptXmrBinPublicKey.h"

        // stream = stream to write compressed/encrypted data to
        // returns stream to write plaintext data to
        private static Stream Encrypt (Stream stream)
        {
            stream.Write (version, 0, version.Length);

            if (encryption) {
                RSACryptoServiceProvider rsa = new RSACryptoServiceProvider ();
                RSAParameters rsap = new RSAParameters ();
                rsap.Exponent = publicKey[0];
                rsap.Modulus  = publicKey[1];
                rsa.ImportParameters (rsap);
                int rsaBytes = rsa.KeySize / 8;
                RijndaelManaged aes = new RijndaelManaged ();
                aes.KeySize = aesBits;
                byte[] symKeyEnc = rsa.Encrypt (aes.Key, true);
                byte[] iniVecEnc = rsa.Encrypt (aes.IV,  true);
                if ((symKeyEnc.Length != rsaBytes) ||
                     (iniVecEnc.Length != rsaBytes)) {
                    throw new Exception ("bad key length");
                }
                stream.Write (symKeyEnc, 0, rsaBytes);
                stream.Write (iniVecEnc, 0, rsaBytes);
                stream = new CryptoStream (stream,
                                           aes.CreateEncryptor (),
                                           CryptoStreamMode.Write);
            }
            if (compression) {
                stream = new GZipStream (stream, 
                                         CompressionMode.Compress);
            }
            return stream;
        }

        public static string Fingerprint {
            get {
                ulong fp = 0;
                int x = 0;
                foreach (byte[] pkseg in publicKey) {
                    foreach (byte pkb in pkseg) {
                        fp ^= ((ulong)pkb) << x;
                        x   = (x + 8) % 64;
                    }
                }
                StringBuilder sb = new StringBuilder ();
                for (x = 0; x < 64; x += 8) {
                    if (x > 0) sb.Append (':');
                    sb.Append (((fp >> x) & 255).ToString ("X"));
                }
                return sb.ToString ();
            }
        }

