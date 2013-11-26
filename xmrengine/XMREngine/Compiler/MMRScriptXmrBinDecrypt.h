
        /*
         * Data for decryption.
         */
#include "MMRScriptXmrBinPrivateKey.h"

        // stream = stream to read compressed/encrypted data from
        // returns stream to read plaintext data from
        public static Stream Decrypt (Stream stream)
        {
            byte[] ver = new byte[version.Length];
            int len = stream.Read (ver, 0, ver.Length);
            if ((len < version.Length) || !memeq (ver, version, ver.Length)) {
                throw new Exception ("script object version mismatch");
            }

            if (encryption) {
                RSACryptoServiceProvider rsa = new RSACryptoServiceProvider ();
                RSAParameters rsap = new RSAParameters ();
                rsap.D        = privateKey[0];
                rsap.DP       = privateKey[1];
                rsap.DQ       = privateKey[2];
                rsap.Exponent = privateKey[3];
                rsap.InverseQ = privateKey[4];
                rsap.Modulus  = privateKey[5];
                rsap.P        = privateKey[6];
                rsap.Q        = privateKey[7];
                rsa.ImportParameters (rsap);
                int rsaBytes = rsa.KeySize / 8;
                RijndaelManaged aes = new RijndaelManaged ();
                aes.KeySize = aesBits;
                byte[] symKeyEnc = new byte[rsaBytes];
                byte[] iniVecEnc = new byte[rsaBytes];
                if ((stream.Read (symKeyEnc, 0, rsaBytes) != rsaBytes) ||
                     (stream.Read (iniVecEnc, 0, rsaBytes) != rsaBytes)) {
                    throw new Exception ("bad key length");
                }
                aes.Key = rsa.Decrypt (symKeyEnc, true);
                aes.IV  = rsa.Decrypt (iniVecEnc, true);
                stream = new CryptoStream (stream,
                                           aes.CreateDecryptor (),
                                           CryptoStreamMode.Read);
            }
            if (compression) {
                stream = new GZipStream (stream,
                                         CompressionMode.Decompress);
            }
            return stream;
        }

        private static bool memeq (byte[] s1, byte[] s2, int len)
        {
            for (int i = 0; i < len; i ++) {
                if (s1[i] != s2[i]) return false;
            }
            return true;
        }
