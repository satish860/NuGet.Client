// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using NuGet.Common;
using NuGet.Packaging.Signing.DerEncoding;

namespace NuGet.Packaging.Signing
{
    /*
        From RFC 5035 (https://tools.ietf.org/html/rfc5035):

            SigningCertificateV2 ::= SEQUENCE {
                certs        SEQUENCE OF ESSCertIDv2,
                policies     SEQUENCE OF PolicyInformation OPTIONAL
            }
    */
    /// <remarks>This is public only to facilitate testing.</remarks>
    public sealed class SigningCertificateV2
    {
        public IReadOnlyList<EssCertIdV2> Certificates { get; }

        private SigningCertificateV2(IReadOnlyList<EssCertIdV2> certificates)
        {
            Certificates = certificates;
        }

        public static SigningCertificateV2 Create(IReadOnlyList<X509Certificate2> chain, HashAlgorithmName hashAlgorithmName)
        {
            if (chain == null || chain.Count == 0)
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(chain));
            }

            var essCertIdV2s = new List<EssCertIdV2>(chain.Count);

            foreach (var certificate in chain)
            {
                var essCertIdV2 = EssCertIdV2.Create(certificate, hashAlgorithmName);

                essCertIdV2s.Add(essCertIdV2);
            }

            return new SigningCertificateV2(essCertIdV2s);
        }

        public static SigningCertificateV2 Read(byte[] bytes)
        {
            var reader = new DerSequenceReader(bytes);

            return Read(reader);
        }

        internal static SigningCertificateV2 Read(DerSequenceReader reader)
        {
            var essCertIdV2Reader = reader.ReadSequence();
            var certificates = ReadCertificates(essCertIdV2Reader);

            // Skip the "policies" field.  We do not use it.

            return new SigningCertificateV2(certificates.AsReadOnly());
        }

        public byte[] Encode()
        {
            var entries = new List<byte[][]>(Certificates.Count);

            foreach (var essCertIdV2 in Certificates)
            {
                entries.Add(essCertIdV2.Encode());
            }

            return DerEncoder.ConstructSequence(DerEncoder.ConstructSegmentedSequence(entries));
        }

        private static List<EssCertIdV2> ReadCertificates(DerSequenceReader reader)
        {
            var certificates = new List<EssCertIdV2>();

            while (reader.HasData)
            {
                var certificate = EssCertIdV2.Read(reader);

                certificates.Add(certificate);
            }

            return certificates;
        }
    }
}