// Copyright 2018 Red Hat, Inc.
// Licensed under the MIT License.

namespace RedHat.OpenShift
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.AspNetCore.Server.Kestrel.Core;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using RedHat.OpenShift.Utils;

    public static class PlatformEnvironment
    {
        public static bool IsOpenShift => !string.IsNullOrEmpty(OpenShiftEnvironment.BuildName);
    }

    public static class OpenShiftEnvironment
    {
        private static string _buildCommit;
        private static string _buildName;
        private static string _buildSource;
        private static string _buildNamespace;
        private static string _buildReference;

        public static string BuildCommit => GetFromEnvironmentVariable("OPENSHIFT_BUILD_COMMIT", ref _buildCommit);
        public static string BuildName => GetFromEnvironmentVariable("OPENSHIFT_BUILD_NAME", ref _buildName);
        public static string BuildSource => GetFromEnvironmentVariable("OPENSHIFT_BUILD_SOURCE", ref _buildSource);
        public static string BuildNamespace => GetFromEnvironmentVariable("OPENSHIFT_BUILD_NAMESPACE", ref _buildNamespace);
        public static string BuildReference => GetFromEnvironmentVariable("OPENSHIFT_BUILD_REFERENCE", ref _buildReference);

        private static string GetFromEnvironmentVariable(string name, ref string cached)
        {
            if (cached == null)
            {
                cached = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            }
            return cached;
        }
    }

    public class OpenShiftIntegrationOptions
    {
        public string CertificateMountPoint { get; set; }

        internal bool UseHttps => !string.IsNullOrEmpty(CertificateMountPoint);
    }

    internal class KestrelOptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly IOptions<OpenShiftIntegrationOptions> _options;
        private readonly OpenShiftCertificateLoader _certificateLoader;

        public KestrelOptionsSetup(IOptions<OpenShiftIntegrationOptions> options, OpenShiftCertificateLoader certificateLoader)
        {
            _options = options;
            _certificateLoader = certificateLoader;
        }

        public void Configure(KestrelServerOptions options)
        {
            if (_options.Value.UseHttps)
            {
                options.ListenAnyIP(8080, configureListen => configureListen.UseHttps(_certificateLoader.ServiceCertificate));
            }
            else
            {
                options.ListenAnyIP(8080);
            }
        }
    }

    internal class OpenShiftCertificateExpiration : Microsoft.Extensions.Hosting.BackgroundService
    {
        private static TimeSpan RestartSpan => TimeSpan.FromMinutes(15);
        private static TimeSpan NotAfterMargin => TimeSpan.FromMinutes(15);
        private readonly IOptions<OpenShiftIntegrationOptions> _options;
        private readonly OpenShiftCertificateLoader _certificateLoader;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly ILogger<OpenShiftCertificateExpiration> _logger;

        public OpenShiftCertificateExpiration(IOptions<OpenShiftIntegrationOptions> options, OpenShiftCertificateLoader certificateLoader, IApplicationLifetime applicationLifetime, ILogger<OpenShiftCertificateExpiration> logger)
        {
            _options = options;
            _certificateLoader = certificateLoader;
            _applicationLifetime = applicationLifetime;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            if (_options.Value.UseHttps)
            {
                try
                {
                    X509Certificate2 certificate = _certificateLoader.ServiceCertificate;
                    bool loop;
                    {
                        loop = false;
                        DateTime expiresAt = certificate.NotAfter - NotAfterMargin; // NotAfter is in local time.
                        DateTime now = DateTime.Now;
                        TimeSpan tillExpires = expiresAt - now;
                        if (tillExpires > TimeSpan.Zero)
                        {
                            if (tillExpires > RestartSpan)
                            {
                                // Wait until we are in the RestartSpan.
                                TimeSpan delay = tillExpires - RestartSpan
                                    + TimeSpan.FromSeconds(new Random().Next((int)RestartSpan.TotalSeconds));
                                if (delay.TotalMilliseconds > int.MaxValue)
                                {
                                    // Task.Delay is limited to int.MaxValue.
                                    await Task.Delay(int.MaxValue, token);
                                    loop = true;
                                }
                                else
                                {
                                    await Task.Delay(delay, token);
                                }
                            }
                        }
                    } while (loop);
                    // Our certificate expired, Stop the application.
                    _logger.LogInformation($"Certificate expires at {certificate.NotAfter.ToUniversalTime()}. Stopping application.");
                    _applicationLifetime.StopApplication();
                }
                catch (TaskCanceledException)
                { }
            }
        }
    }

    internal class OpenShiftCertificateLoader
    {
        private readonly IOptions<OpenShiftIntegrationOptions> _options;
        private X509Certificate2 _certificate;

        public OpenShiftCertificateLoader(IOptions<OpenShiftIntegrationOptions> options)
        {
            _options = options;
        }

        public X509Certificate2 ServiceCertificate
        {
            get
            {
                if (_certificate == null)
                {
                    if (_options.Value.UseHttps)
                    {
                        string certificateMountPoint = _options.Value.CertificateMountPoint;
                        string certificateFile = Path.Combine(certificateMountPoint, "tls.crt");
                        string keyFile = Path.Combine(certificateMountPoint, "tls.key");
                        _certificate = CertificateLoader.LoadCertificateWithKey(certificateFile, keyFile);
                    }
                }
                return _certificate;
            }
        }
    }
}

namespace RedHat.OpenShift.Utils
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using Org.BouncyCastle.Crypto;
    using Org.BouncyCastle.Crypto.Parameters;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.OpenSsl;

    public static class CertificateLoader
    {
        public static X509Certificate2 LoadCertificateWithKey(string certificateFile, string keyFile)
        {
            var certificate = new X509Certificate2(certificateFile);
            return certificate.CopyWithPrivateKey(ReadPrivateKeyAsRSA(keyFile));
        }

        private static RSA ReadPrivateKeyAsRSA(string keyFile)
        {
            using (var reader = new StreamReader(new MemoryStream(File.ReadAllBytes(keyFile))))
            {
                var obj = new PemReader(reader).ReadObject();
                if (obj is AsymmetricCipherKeyPair) {
                    var cipherKey = (AsymmetricCipherKeyPair)obj;
                    obj = cipherKey.Private;
                }
                var privKey = (RsaPrivateCrtKeyParameters)obj;
                return RSA.Create(DotNetUtilities.ToRSAParameters(privKey));
            }
        }

        private static class DotNetUtilities
        {
            /*
            +    // This class was derived from:
            +    // https://github.com/bcgit/bc-csharp/blob/master/crypto/src/security/DotNetUtilities.cs
            +    // License:
            +    // The Bouncy Castle License
            +    // Copyright (c) 2000-2018 The Legion of the Bouncy Castle Inc.
            +    // (https://www.bouncycastle.org)
            +    // Permission is hereby granted, free of charge, to any person obtaining a
            +    // copy of this software and associated documentation files (the "Software"), to deal in the
            +    // Software without restriction, including without limitation the rights to use, copy, modify, merge,
            +    // publish, distribute, sub license, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
            +    // The above copyright notice and this permission notice shall be included
            +    // in all copies or substantial portions of the Software.
            */

            public static RSAParameters ToRSAParameters(RsaPrivateCrtKeyParameters privKey)
            {
                RSAParameters rp = new RSAParameters();
                rp.Modulus = privKey.Modulus.ToByteArrayUnsigned();
                rp.Exponent = privKey.PublicExponent.ToByteArrayUnsigned();
                rp.P = privKey.P.ToByteArrayUnsigned();
                rp.Q = privKey.Q.ToByteArrayUnsigned();
                rp.D = ConvertRSAParametersField(privKey.Exponent, rp.Modulus.Length);
                rp.DP = ConvertRSAParametersField(privKey.DP, rp.P.Length);
                rp.DQ = ConvertRSAParametersField(privKey.DQ, rp.Q.Length);
                rp.InverseQ = ConvertRSAParametersField(privKey.QInv, rp.Q.Length);
                return rp;
            }

            private static byte[] ConvertRSAParametersField(BigInteger n, int size)
            {
                byte[] bs = n.ToByteArrayUnsigned();

                if (bs.Length == size)
                    return bs;

                if (bs.Length > size)
                    throw new ArgumentException("Specified size too small", "size");

                byte[] padded = new byte[size];
                Array.Copy(bs, 0, padded, size - bs.Length, bs.Length);
                return padded;
            }
        }
    }
}

namespace Microsoft.AspNetCore.Hosting
{
    using System;
    using Microsoft.AspNetCore.Server.Kestrel.Core;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using RedHat.OpenShift;

    public static class OpenShiftWebHostBuilderExtensions
    {
        public static IWebHostBuilder UseOpenShiftIntegration(this IWebHostBuilder builder, Action<OpenShiftIntegrationOptions> configureOptions)
        {
            if (configureOptions == null)
            {
                throw new ArgumentNullException(nameof(configureOptions));
            }

            if (PlatformEnvironment.IsOpenShift)
            {
                // Clear the urls. We'll explicitly configure Kestrel depending on the options.
                builder.UseUrls(new string[]{});

                builder.ConfigureServices(services =>
                {
                    services.Configure(configureOptions);
                    services.AddSingleton<OpenShiftCertificateLoader>();
                    services.AddSingleton<IConfigureOptions<KestrelServerOptions>, KestrelOptionsSetup>();
                    services.AddSingleton<IHostedService, OpenShiftCertificateExpiration>();
                });
            }
            return builder;
        }
    }
}