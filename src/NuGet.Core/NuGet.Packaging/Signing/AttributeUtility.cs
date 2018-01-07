// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
#if IS_DESKTOP
using System.Security.Cryptography.Pkcs;
#endif
using System.Security.Cryptography.X509Certificates;
using NuGet.Common;
using NuGet.Packaging.Signing.DerEncoding;

namespace NuGet.Packaging.Signing
{
    public static class AttributeUtility
    {
#if IS_DESKTOP
        /// <summary>
        /// Create a CommitmentTypeIndication attribute.
        /// https://tools.ietf.org/html/rfc5126.html#section-5.11.1
        /// </summary>
        public static CryptographicAttributeObject GetCommitmentTypeIndication(SignatureType type)
        {
            // SignatureType -> Oid
            var valueOid = GetSignatureTypeOid(type);

            // DER encode the signature type Oid in a sequence.
            // CommitmentTypeQualifier ::= SEQUENCE {
            // commitmentTypeIdentifier CommitmentTypeIdentifier,
            // qualifier                  ANY DEFINED BY commitmentTypeIdentifier }
            var commitmentTypeData = DerEncoder.ConstructSequence(new List<byte[][]>() { DerEncoder.SegmentedEncodeOid(valueOid) });
            var data = new AsnEncodedData(Oids.CommitmentTypeIndication, commitmentTypeData);

            // Create an attribute
            return new CryptographicAttributeObject(
                oid: new Oid(Oids.CommitmentTypeIndication),
                values: new AsnEncodedDataCollection(data));
        }

        /// <summary>
        /// Oid -> SignatureType
        /// </summary>
        /// <remarks>Unknown Oids are ignored. Throws for empty values and invalid combinations.</remarks>
        public static SignatureType GetCommitmentTypeIndication(CryptographicAttributeObject attribute)
        {
            if (!IsValidCommitmentTypeIndication(attribute))
            {
                throw new SignatureException(Strings.CommitmentTypeIndicationAttributeInvalid);
            }

            // Remove unknown values, these could be future values.
            // Invalid combinations and empty checks have already been done.
            var knownValues = GetCommitmentTypeIndicationRawValues(attribute)
                .Where(e => e != SignatureType.Unknown)
                .ToList();

            // Return the only recognized value.
            if (knownValues.Count == 1)
            {
                return knownValues[0];
            }

            // All values were unknown
            return SignatureType.Unknown;
        }

        internal static SignatureType GetCommitmentTypeIndication(SignerInfo signer)
        {
            var commitmentTypeIndication = signer.SignedAttributes.GetAttributeOrDefault(Oids.CommitmentTypeIndication);
            if (commitmentTypeIndication != null)
            {
                return GetCommitmentTypeIndication(commitmentTypeIndication);
            }

            return SignatureType.Unknown;
        }

        /// <summary>
        /// True if the commitment-type-indication value does not
        /// contain an invalid combination of values. Unknown
        /// values are ignored.
        /// </summary>
        public static bool IsValidCommitmentTypeIndication(CryptographicAttributeObject attribute)
        {
            var values = GetCommitmentTypeIndicationRawValues(attribute);

            // Zero values is invalid.
            if (values.Count < 1)
            {
                return false;
            }

            // Remove unknown values, these could be future values.
            var knownValues = values.Where(e => e != SignatureType.Unknown).ToList();

            // Currently the value must be a single value of author or repository. If multiple
            // known values exist then either there is a duplicate or both author and repository
            // was listed in the attribute.
            if (knownValues.Count > 1)
            {
                return false;
            }

            // A known or unknown value is present, and no invalid combinations exist.
            return true;
        }

        /// <summary>
        /// Oid -> SignatureType
        /// </summary>
        public static SignatureType GetSignatureType(string oid)
        {
            switch (oid)
            {
                case Oids.CommitmentTypeIdentifierProofOfOrigin:
                    return SignatureType.Author;
                case Oids.CommitmentTypeIdentifierProofOfReceipt:
                    return SignatureType.Repository;
                default:
                    return SignatureType.Unknown;
            }
        }

        /// <summary>
        /// SignatureType -> Oid
        /// </summary>
        public static string GetSignatureTypeOid(SignatureType signatureType)
        {
            switch (signatureType)
            {
                case SignatureType.Author:
                    return Oids.CommitmentTypeIdentifierProofOfOrigin;
                case SignatureType.Repository:
                    return Oids.CommitmentTypeIdentifierProofOfReceipt;
                default:
                    throw new ArgumentException(nameof(signatureType));
            }
        }

        /// <summary>
        /// Create a signing-certificate-v2 from a certificate.
        /// </summary>
        public static CryptographicAttributeObject GetSigningCertificateV2(
            IReadOnlyList<X509Certificate2> chain,
            Common.HashAlgorithmName hashAlgorithm)
        {
            if (chain == null || chain.Count == 0)
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(chain));
            }

            var signingCertificateV2 = SigningCertificateV2.Create(chain, hashAlgorithm);
            var bytes = signingCertificateV2.Encode();

            var data = new AsnEncodedData(Oids.SigningCertificateV2, bytes);

            return new CryptographicAttributeObject(
                new Oid(Oids.SigningCertificateV2),
                new AsnEncodedDataCollection(data));
        }

        /// <summary>
        /// Verify a signing-certificate-v2 attribute.
        /// </summary>
        public static bool IsValidSigningCertificateV2(
            X509Certificate2 signatureCertificate,
            IReadOnlyList<X509Certificate2> chain,
            CryptographicAttributeObject signingCertV2Attribute,
            SigningSpecifications signingSpecifications)
        {
            return IsValidSigningCertificateV2(
                signatureCertificate,
                chain,
                GetESSCertIDv2Entries(signingCertV2Attribute),
                signingSpecifications);
        }

        /// <summary>
        /// Verify components of a signing-certificate-v2 attribute.
        /// </summary>
        public static bool IsValidSigningCertificateV2(
            X509Certificate2 signatureCertificate,
            IReadOnlyList<X509Certificate2> localCertificateChain,
            IReadOnlyList<KeyValuePair<Common.HashAlgorithmName, byte[]>> attributeCertificateChain,
            SigningSpecifications signingSpecifications)
        {
            // Verify chain counts to avoid unneeded hashing
            if (localCertificateChain.Count != attributeCertificateChain.Count || attributeCertificateChain.Count < 1)
            {
                return false;
            }

            // Check each entry in the chain
            for (var i = 0; i < attributeCertificateChain.Count; i++)
            {
                var attributeEntry = attributeCertificateChain[i];
                var localEntry = localCertificateChain[i];

                // Verify hash algorithm is allowed
                if (!signingSpecifications.AllowedHashAlgorithmOids.Contains(
                    attributeEntry.Key.ConvertToOidString(),
                    StringComparer.Ordinal))
                {
                    return false;
                }

                // Hash the local cert using the attribute hash algorithm
                var localValue = GetESSCertIDv2Entry(localEntry, attributeEntry.Key);

                // Verify the hashes match
                if (!VerifyHash(localValue, attributeEntry))
                {
                    return false;
                }

                // Verify the first entry is the leaf cert used
                if (i == 0)
                {
                    var leafHash = GetESSCertIDv2Entry(signatureCertificate, attributeEntry.Key);

                    if (!VerifyHash(leafHash, attributeEntry))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the first attribute if the Oid is found.
        /// Returns null if the attribute is not found.
        /// </summary>
        internal static CryptographicAttributeObject GetAttributeOrDefault(this CryptographicAttributeObjectCollection attributes, string oid)
        {
            if (oid == null)
            {
                throw new ArgumentNullException(nameof(oid));
            }

            foreach (var attribute in attributes)
            {
                if (StringComparer.Ordinal.Equals(oid, attribute.Oid.Value))
                {
                    return attribute;
                }
            }

            return null;
        }

        /// <summary>
        /// Compare hash values.
        /// </summary>
        private static bool VerifyHash(KeyValuePair<Common.HashAlgorithmName, byte[]> a, KeyValuePair<Common.HashAlgorithmName, byte[]> b)
        {
            return VerifyHash(a.Value, b.Value);
        }

        /// <summary>
        /// Compare hash values.
        /// </summary>
        private static bool VerifyHash(byte[] a, byte[] b)
        {
            return a.SequenceEqual(b);
        }

        /// <summary>
        /// CryptographicAttributeObject -> DerSequenceReader
        /// </summary>
        internal static DerSequenceReader ToDerSequenceReader(this CryptographicAttributeObject attribute)
        {
            var values = attribute.Values.ToList();

            if (values.Count != 1)
            {
                ThrowInvalidAttributeException(attribute);
            }

            return new DerSequenceReader(values[0].RawData);
        }

        /// <summary>
        /// Throw a signature exception due to an invalid attribute. This is used for unusual situations
        /// where the format is corrupt.
        /// </summary>
        private static void ThrowInvalidAttributeException(CryptographicAttributeObject attribute)
        {
            throw new SignatureException(string.Format(CultureInfo.CurrentCulture, Strings.SignatureContainsInvalidAttribute, attribute.Oid.Value));
        }

        /// <summary>
        /// Enumerate AsnEncodedDataCollection
        /// </summary>
        private static List<AsnEncodedData> ToList(this AsnEncodedDataCollection collection)
        {
            var values = new List<AsnEncodedData>();

            foreach (var value in collection)
            {
                values.Add(value);
            }

            return values;
        }

        // ESSCertIDv2::=  SEQUENCE {
        //    hashAlgorithm AlgorithmIdentifier
        //           DEFAULT {algorithm id-sha256 },
        //    certHash Hash,
        //    issuerSerial IssuerSerial OPTIONAL
        // }
        private static byte[][] CreateESSCertIDv2Entry(X509Certificate2 cert, Common.HashAlgorithmName hashAlgorithm)
        {
            // Get hash Oid
            var hashAlgorithmOid = hashAlgorithm.ConvertToOidString();
            var hash = CertificateUtility.GetHash(cert, hashAlgorithm);
            var serialNumber = cert.GetSerialNumber();

            // Convert from little endian to big endian.
            Array.Reverse(serialNumber);

            return DerEncoder.ConstructSegmentedSequence(new List<byte[][]>()
            {
                DerEncoder.ConstructSegmentedSequence(new List<byte[][]>()
                {
                    // AlgorithmIdentifier
                    DerEncoder.ConstructSegmentedSequence(new List<byte[][]>()
                    {
                        DerEncoder.SegmentedEncodeOid(hashAlgorithmOid)
                    }),

                    // Hash
                    DerEncoder.SegmentedEncodeOctetString(hash),

                    // IssuerSerial
                    DerEncoder.ConstructSegmentedSequence(new List<byte[][]>()
                    {
                        DerEncoder.ConstructSegmentedSequence(new List<byte[][]>()
                        {
                            // GeneralNames
                            DerEncoder.ConstructSegmentedContextSpecificValue(contextId: 4, items: new byte[][] { cert.IssuerName.RawData })
                        }),

                        DerEncoder.SegmentedEncodeUnsignedInteger(serialNumber)
                    })
                })
            });
        }

        // Cert -> Hash pair
        private static KeyValuePair<Common.HashAlgorithmName, byte[]> GetESSCertIDv2Entry(X509Certificate2 cert, Common.HashAlgorithmName hashAlgorithm)
        {
            var hashValue = CertificateUtility.GetHash(cert, hashAlgorithm);

            return new KeyValuePair<Common.HashAlgorithmName, byte[]>(hashAlgorithm, hashValue);
        }

        // Attribute -> Hashes
        public static List<KeyValuePair<Common.HashAlgorithmName, byte[]>> GetESSCertIDv2Entries(CryptographicAttributeObject attribute)
        {
            if (attribute == null)
            {
                throw new ArgumentNullException(nameof(attribute));
            }

            if (!StringComparer.Ordinal.Equals(Oids.SigningCertificateV2, attribute.Oid.Value))
            {
                throw new ArgumentException(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.SigningCertificateV2AttributeRequired,
                    Oids.SigningCertificateV2), nameof(attribute));
            }

            var entries = new List<KeyValuePair<Common.HashAlgorithmName, byte[]>>();
            var reader = attribute.ToDerSequenceReader();

            var signingCertificateV2 = SigningCertificateV2.Read(reader);

            foreach (var certificate in signingCertificateV2.Certificates)
            {
                var entry = GetESSCertIDv2Entry(certificate);

                entries.Add(entry);
            }

            return entries;
        }

        private static KeyValuePair<Common.HashAlgorithmName, byte[]> GetESSCertIDv2Entry(EssCertIdV2 essCertIdV2)
        {
            var hashAlgorithm = CryptoHashUtility.OidToHashAlgorithmName(essCertIdV2.HashAlgorithm.Algorithm.Value);

            return new KeyValuePair<Common.HashAlgorithmName, byte[]>(hashAlgorithm, essCertIdV2.CertificateHash);
        }

        /// <summary>
        /// Attribute -> SignatureType values with no validation.
        /// </summary>
        private static List<SignatureType> GetCommitmentTypeIndicationRawValues(CryptographicAttributeObject attribute)
        {
            var values = new List<SignatureType>(1);
            var reader = attribute.ToDerSequenceReader();

            while (reader.HasData)
            {
                values.Add(GetSignatureType(reader.ReadOidAsString()));
            }

            return values;
        }
#endif
    }
}