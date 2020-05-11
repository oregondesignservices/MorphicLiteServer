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
using System.Security.Cryptography;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace MorphicServer
{
    /// <summary>
    /// Class to hash and compare data.
    /// </summary>
    public class HashedData
    {
        /// <summary>
        /// Number of iterations for the hash functions
        /// </summary>
        public int IterationCount { get; }
        /// <summary>
        /// The hash function to use. Currently supported: see Pbkdf2Sha512
        /// </summary>
        public string HashFunction { get; }
        /// <summary>
        /// Salt to add to the hashing (google Rainbow Tables)
        /// </summary>
        public string Salt { get; }
        /// <summary>
        /// The hashed data
        /// </summary>
        public string Hash { get; }

        private const String Pbkdf2Sha512 = "PBKDF2-SHA512";
        private const int IterationCountPbkdf2 = 10000;

        class HashedDataException : Exception
        {
            public HashedDataException(String error) : base(error)
            {
            }
        }

        /// <summary>
        /// Create a HashedData object from data and optionally salt.
        /// </summary>
        /// <param name="data">the data to hash</param>
        /// <param name="salt">(Optional) If not provided, a random salt will be created</param>
        /// <returns></returns>
        public static HashedData FromString(string data, string? salt = null)
        {
            var s = salt ?? RandomSalt();
            return new HashedData(IterationCountPbkdf2, Pbkdf2Sha512, s,
                DoHash(IterationCountPbkdf2, Pbkdf2Sha512, s, data));
        }

        public HashedData(int iterationCount, string hashFunction, string salt, string hash)
        {
            HashFunction = hashFunction;
            Salt = salt;
            IterationCount = iterationCount;
            Hash = hash;
        }

        public static HashedData FromCombinedString(String hashedCombinedString)
        {
            var parts = hashedCombinedString.Split(":");
            if (parts.Length != 4)
            {
                throw new HashedDataException("combined string does not have enough parts");
            }

            int iterations = Int32.Parse(parts[1]);
            return new HashedData(iterations, parts[0], parts[2], parts[3]);
        }

        public string ToCombinedString()
        {
            return $"{HashFunction}:{IterationCount}:{Salt}:{Hash}";
        }

        public bool Equals(string data)
        {
            var hash = DoHash(IterationCount, HashFunction, Salt, data);
            return hash == Hash;
        }

        private static string DoHash(int iterations, string hashFunction, string salt, string data)
        {
            KeyDerivationPrf function;
            int keyLength;

            if (hashFunction == Pbkdf2Sha512)
            {
                function = KeyDerivationPrf.HMACSHA512;
                keyLength = 64;
            }
            else
            {
                throw new Exception("Invalid Key Derivation Function");
            }

            var s = Convert.FromBase64String(salt);
            var h = KeyDerivation.Pbkdf2(data, s, function, iterations, keyLength);
            return Convert.ToBase64String(h);
        }

        private static string RandomSalt()
        {
            var salt = new byte[16];
            var provider = RandomNumberGenerator.Create();
            provider.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }

        /// <summary>
        /// Custom Bson Serializer that converts a HashedData to and from its combined string representation
        /// </summary>
        public class BsonSerializer: SerializerBase<HashedData>
        {
            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, HashedData value)
            {
                context.Writer.WriteString(value.ToCombinedString());
            }

            public override HashedData Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
            {
                return HashedData.FromCombinedString(context.Reader.ReadString());
            }
        }

        public static bool operator==(HashedData a, string b)
        {
            return a.ToCombinedString() == b;
        }

        public static bool operator!=(HashedData a, string b)
        {
            return a.ToCombinedString() != b;
        }

        public override bool Equals(object? other)
        {
            if (other is HashedData hash)
            {
                return this.ToCombinedString() == hash.ToCombinedString();
            }
            if (other is string hashString){
                return this == hashString;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return ToCombinedString().GetHashCode();
        }
    }
}