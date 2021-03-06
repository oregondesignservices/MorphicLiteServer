// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt
//
// The R&D leading to these results received funding from the:
// * Rehabilitation Services Administration, US Dept. of Education under 
//   grant H421A150006 (APCP)
// * National Institute on Disability, Independent Living, and 
//   Rehabilitation Research (NIDILRR)
// * Administration for Independent Living & Dept. of Education under grants 
//   H133E080022 (RERC-IT) and H133E130028/90RE5003-01-00 (UIITA-RERC)
// * European Union's Seventh Framework Programme (FP7/2007-2013) grant 
//   agreement nos. 289016 (Cloud4all) and 610510 (Prosperity4All)
// * William and Flora Hewlett Foundation
// * Ontario Ministry of Research and Innovation
// * Canadian Foundation for Innovation
// * Adobe Foundation
// * Consumer Electronics Association Foundation

using System;
using System.IO;
using System.Security.Cryptography;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
using Prometheus;

namespace Morphic.Security
{
    /// <summary>
    /// EncryptedField class: Uses AES-256-CBC encryption with an IV and a share secret.
    /// 
    /// Encryption is always with the primary (env-var "MORPHIC_ENC_KEY_PRIMARY") key.
    ///
    /// Decryption is with the name of the key used before. An indication is given when a
    /// field is decrypted and found to be using a non-primary key.
    ///
    /// Key Rollover: When after decryption the caller sees that the decryption key used was not
    /// the primary, the caller should then proceed to re-encrypting the data (which will then use
    /// the primary key). Thus, key-rollover is achieved one by one. If necessary, a data-migration
    /// framework may need to be provided (future work) so that data can be re-encrypted in bulk in
    /// one go.
    ///
    /// It is up to the caller to save the encrypted data any way they want. For ease of use
    /// "combinedString" functions are provided which combine the various fields into a
    /// colon-delimited fields (none of the fields can contain colons, so this is safe).
    ///
    /// The KeyStorage class is used to manage the keys.
    /// </summary>
    public class EncryptedField
    {
        private const string Aes256CbcString = "AES-256-CBC";

        private const string EncryptionTimingMetricsName = "encryption_duration";
        private static readonly Histogram EncryptionHistogram = Metrics.CreateHistogram(
            EncryptionTimingMetricsName,
            "Encryption Duration",
            new[] {"cipher"});

        private const string DecryptionTimingMetricsName = "decryption_duration";
        private static readonly Histogram DecryptionHistogram = Metrics.CreateHistogram(
            DecryptionTimingMetricsName,
            "Decryption Duration",
            new[] {"cipher"});

        /// <summary>
        /// Initialize the object from constituent parts.
        /// </summary>
        /// <param name="keyName">The name of the key. This is used to find the decryption key later.</param>
        /// <param name="cipher">The cipher (and mode) to use.</param>
        /// <param name="iv">Initialization Vector (IV)</param>
        /// <param name="cipherText">The encrypted data</param>
        public EncryptedField(string keyName, string cipher, string iv, string cipherText)
        {
            KeyName = keyName;
            Cipher = cipher;
            Iv = iv;
            CipherText = cipherText;
        }

        /// <summary>
        /// The Name of the key that encrypted the data. This is used to find the decryption key later.
        /// </summary>
        public string KeyName { get; }

        /// <summary>
        /// The Cipher (and mode) used. Currently supported: see Aes256CbcString
        /// </summary>
        public string Cipher { get; }

        /// <summary>
        /// The Initialization Vector (IV) for the encryption.
        /// </summary>
        public string Iv { get; }

        /// <summary>
        /// The encrypted text.
        /// </summary>
        public string CipherText { get; }

        /// <summary>
        /// Create a new EncryptedField class from plaintext. The returned object contains the encrypted data.
        /// </summary>
        /// <param name="plainText"></param>
        /// <returns></returns>
        public static EncryptedField FromPlainText(string plainText)
        {
            var iv = Random128BitsBase64();
            var key = KeyStorage.Shared.GetPrimary();
            using (EncryptionHistogram.Labels(Aes256CbcString).NewTimer())
            {
                var encryptedData = new EncryptedField(
                    key.KeyName,
                    Aes256CbcString,
                    iv,
                    Convert.ToBase64String(
                        EncryptStringToBytes_Aes256CBC(
                            plainText,
                            key.KeyData, // we always encrypt with the primary
                            Convert.FromBase64String(iv))));
                return encryptedData;
            }
        }

        /// <summary>
        /// Import the encrypted data from a previously combined string of format "{Cipher}:{KeyName}:{Iv}:{CipherText}"
        /// </summary>
        /// <param name="combinedString"></param>
        /// <returns></returns>
        public static EncryptedField FromCombinedString(string combinedString)
        {
            var parts = combinedString.Split(":");
            var encryptedField = new EncryptedField(
                parts[1],
                parts[0],
                parts[2],
                parts[3]);
            return encryptedField;
        }

        /// <summary>
        /// Convert the data into a colon-delimited string: "{Cipher}:{KeyName}:{Iv}:{CipherText}"
        /// </summary>
        /// <returns></returns>
        public string ToCombinedString()
        {
            return $"{Cipher}:{KeyName}:{Iv}:{CipherText}";
        }

        /// <summary>
        /// Decrypt the data in the EncryptedField class.
        /// </summary>
        /// <param name="isPrimary">Indicates whether the text was encrypted with the primary key or not.
        /// Caller should re-encrypt with the primary key if this is returned false</param>
        /// <returns>the plainText string</returns>
        /// <exception cref="UnknownCipherModeException"></exception>
        public string Decrypt()
        {
            if (Cipher == Aes256CbcString)
            {
                using (DecryptionHistogram.Labels(Aes256CbcString).NewTimer())
                {
                    var keyInfo = KeyStorage.Shared.GetKey(KeyName);
                    if (!keyInfo.IsPrimary)
                    {
                        // do nothing (assume some background job is running?) or trigger some background to migrate old to new 
                    }
                    var plainText = DecryptStringFromBytes_Aes256CBC(
                        Convert.FromBase64String(CipherText),
                        keyInfo.KeyData,
                        Convert.FromBase64String(Iv));
                    return plainText;
                }
            }

            throw new UnknownCipherModeException(Cipher);
        }

        // Helper functions

        /// <summary>
        /// Encrypt data using AES-256-CBC
        /// </summary>
        /// <param name="plainText">The plaintext</param>
        /// <param name="key">they key in bytes</param>
        /// <param name="iv">they IV in bytes</param>
        /// <returns></returns>
        /// <exception cref="PlainTextEmptyException"></exception>
        private static byte[] EncryptStringToBytes_Aes256CBC(string plainText, byte[] key, byte[] iv)
        {
            // Check arguments.
            if (plainText == null)
                throw new PlainTextEmptyException("plainText");
            if (key == null || key.Length <= 16)
                throw new KeyArgumentBad("key");
            if (iv == null || iv.Length <= 0)
                throw new IvArgumentBad("iv");
            byte[] encrypted;

            // Create an AesCryptoServiceProvider object
            // with the specified key and IV.
            using (var aesAlg = new AesCryptoServiceProvider())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;
                aesAlg.Mode = CipherMode.CBC;

                // Create an encryptor to perform the stream transform.
                var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (var msEncrypt = new MemoryStream())
                {
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }

                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }

        /// <summary>
        /// data using AES-256-CBC
        /// </summary>
        /// <param name="cipherText">The encrypted data</param>
        /// <param name="key">they key in bytes</param>
        /// <param name="iv">they IV in bytes</param>
        /// <returns></returns>
        /// <exception cref="CipherTextEmptyException"></exception>
        private static string DecryptStringFromBytes_Aes256CBC(byte[] cipherText, byte[] key, byte[] iv)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new CipherTextEmptyException("cipherText");
            if (key == null || key.Length <= 0)
                throw new KeyArgumentBad("key");
            if (iv == null || iv.Length <= 0)
                throw new IvArgumentBad("iv");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext;

            // Create an AesCryptoServiceProvider object
            // with the specified key and IV.
            using (var aesAlg = new AesCryptoServiceProvider())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;
                aesAlg.Mode = CipherMode.CBC;

                // Create a decryptor to perform the stream transform.
                var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (var msDecrypt = new MemoryStream(cipherText))
                {
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (var srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            return plaintext;
        }

        /// <summary>
        /// Create a random IV of size 16 bytes (suitable for AES-256 encryption).
        /// </summary>
        /// <returns></returns>
        public static string Random128BitsBase64()
        {
            var iv = RandomBytes(16);
            return Convert.ToBase64String(iv);
        }

        public static byte[] RandomBytes(int n)
        {
            var bytes = new byte[n];
            var provider = RandomNumberGenerator.Create();
            provider.GetBytes(bytes);
            return bytes;
        }
        
        // custom exceptions for this class.

        public class EncryptedFieldException : Exception
        {
            protected EncryptedFieldException(string error) : base(error)
            {
            }
        }

        public class PlainTextEmptyException : EncryptedFieldException
        {
            public PlainTextEmptyException(string error) : base(error)
            {
            }
        }

        public class CipherTextEmptyException : EncryptedFieldException
        {
            public CipherTextEmptyException(string error) : base(error)
            {
            }
        }

        public class KeyArgumentBad : EncryptedFieldException
        {
            public KeyArgumentBad(string error) : base(error)
            {
            }
        }

        public class IvArgumentBad : EncryptedFieldException
        {
            public IvArgumentBad(string error) : base(error)
            {
            }
        }

        private class UnknownCipherModeException : EncryptedFieldException
        {
            public UnknownCipherModeException(string error) : base(error)
            {
            }
        }

        /// <summary>
        /// Custom Bson Serializer that converts an EncryptedField to and from its combined string representation
        /// </summary>
        public class BsonSerializer: SerializerBase<EncryptedField>
        {
            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, EncryptedField value)
            {
                if (value == null){
                    context.Writer.WriteNull();
                }else{
                    context.Writer.WriteString(value.ToCombinedString());
                }
            }

            public override EncryptedField Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
            {
                if (context.Reader.CurrentBsonType == BsonType.Null){
                    context.Reader.ReadNull();
                    return null!;
                }
                return EncryptedField.FromCombinedString(context.Reader.ReadString());
            }
        }
    }
}