// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Signing;
using NuGet.Test.Utility;

namespace NuGet.Packaging.Test.SigningTests
{
    public static class SignTestUtility
    {
        private const string _internalTimestampServer = "http://rfc3161.gtm.corp.microsoft.com/TSS/HttpTspServer";

        /// <summary>
        /// Sign a package for test purposes.
        /// </summary>
        public static async Task SignPackageAsync(TestLogger testLogger, X509Certificate2 cert, SignedPackageArchive signPackage)
        {
            var testSignatureProvider = new X509SignatureProvider(new Rfc3161TimestampProvider(new Uri(_internalTimestampServer)));
            var signer = new Signer(signPackage, testSignatureProvider);

            var request = new SignPackageRequest()
            {
                Certificate = cert,
                SignatureHashAlgorithm = Common.HashAlgorithmName.SHA256
            };

            await signer.SignAsync(request, testLogger, CancellationToken.None);
        }
    }
}