﻿// HttpUpdateProvider.cs, 20.06.2019
// Copyright (C) Dominic Beger 20.06.2019

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using nUpdate.Properties;

namespace nUpdate
{
    /// <summary>
    ///     Provides functionality to update .NET-applications with a server accessed through HTTP(S).
    /// </summary>
    public class HttpUpdateProvider : IUpdateProvider
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public HttpUpdateProvider(IHttpUpdateDeliveryEndpoint updateDeliveryEndpoint, string publicKey, IVersion applicationVersion)
        {
            UpdateDeliveryEndpoint = updateDeliveryEndpoint ?? throw new ArgumentNullException(nameof(updateDeliveryEndpoint));
            ApplicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));

            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException(nameof(publicKey));
            PublicKey = publicKey;

            CreateAppUpdateDirectory();
        }

        /// <summary>
        ///     Gets or sets the host application options during the update installation.
        /// </summary>
        public static HostApplicationOptions HostApplicationOptions { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether pre-releases should be included into the update check.
        /// </summary>
        public bool IncludePreRelease { get; set; }

        /// <summary>
        ///     Gets or sets the public key used for verifying the update packages.
        /// </summary>
        public string PublicKey { get; set; }

        /// <summary>
        ///     Gets the version of the current application.
        /// </summary>
        public IVersion ApplicationVersion { get; set; }

        /// <summary>
        ///     Gets or sets the <see cref="CultureInfo" /> of the language to use.
        /// </summary>
        public CultureInfo LanguageCulture { get; set; } = CultureInfo.CurrentUICulture;

        public IUpdateDeliveryEndpoint UpdateDeliveryEndpoint { get; }

        public async Task<UpdateCheckResult> CheckForUpdates(CancellationToken cancellationToken)
        {
            return await UpdateDeliveryEndpoint.CheckForUpdates(ApplicationVersion, IncludePreRelease,
                cancellationToken);
        }

        public Task DownloadUpdates(UpdateCheckResult checkResult, CancellationToken cancellationToken, IProgress<UpdateProgressData> progress)
        {
            if (checkResult == null)
                throw new InvalidOperationException(nameof(checkResult));
            return UpdateDeliveryEndpoint.DownloadPackages(checkResult, progress,
                cancellationToken);
        }

        public async Task InstallUpdates(UpdateCheckResult checkResult, Action terminateAction = null)
        {
            var updatePackages = checkResult.Packages.ToArray();
            await updatePackages.ForEachAsync(p =>
            {
                return Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(GetLocalPackagePath(p),
                        Path.Combine(Globals.ApplicationUpdateDirectory, p.Guid.ToString()));
                });
            });
        }

        /// <summary>
        ///     Determines whether all available packages are valid, or not.
        /// </summary>
        /// <returns>A <see cref="VerificationResult" /> containing information about the validation of all packages.</returns>
        /// <exception cref="FileNotFoundException">One of the specified update packages could not be found.</exception>
        /// <exception cref="ArgumentException">The signature of an update package is <c>null</c> or empty.</exception>
        /// <seealso cref="RemoveLocalPackage" />
        /// <seealso cref="VerifyUpdate" />
        public Task<VerificationResult> VerifyUpdates(UpdateCheckResult checkResult)
        {
            return VerifyUpdates(checkResult.Packages);
        }

        private string GetLocalPackagePath(UpdatePackage package)
        {
            return Path.Combine(Globals.ApplicationUpdateDirectory,
                $"{package.Guid}{Globals.PackageExtension}");
        }

        /// <summary>
        ///     Removes the specified update package.
        /// </summary>
        /// <param name="package">The update package.</param>
        /// <seealso cref="RemoveLocalPackages(UpdateCheckResult)" />
        /// <seealso cref="RemoveLocalPackages(IEnumerable{UpdatePackage})" />
        public void RemoveLocalPackage(UpdatePackage package)
        {
            var path = GetLocalPackagePath(package);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        ///     Removes the specified update packages.
        /// </summary>
        /// <param name="packages">The update packages.</param>
        /// <seealso cref="RemoveLocalPackage" />
        /// <seealso cref="RemoveLocalPackages(UpdateCheckResult)" />
        public void RemoveLocalPackages(IEnumerable<UpdatePackage> packages)
        {
            if (packages == null)
                throw new ArgumentNullException(nameof(packages));
            foreach (var package in packages)
                RemoveLocalPackage(package);
        }

        /// <summary>
        ///     Removes all downloaded update packages.
        /// </summary>
        /// <seealso cref="RemoveLocalPackage" />
        /// <seealso cref="RemoveLocalPackages(IEnumerable{UpdatePackage})" />
        public void RemoveLocalPackages(UpdateCheckResult checkResult)
        {
            RemoveLocalPackages(checkResult.Packages);
        }

        /// <summary>
        ///     Creates the application's update directory where the downloaded packages and other files are stored.
        /// </summary>
        private void CreateAppUpdateDirectory()
        {
            Logger.Info("Checking existence of the application's update directory.");
            if (Directory.Exists(Globals.ApplicationUpdateDirectory))
                return;

            Logger.Info("Creating the application's update directory.");
            try
            {
                Directory.CreateDirectory(Globals.ApplicationUpdateDirectory);
            }
            catch (Exception ex)
            {
                Logger.Error(ex,
                    "The application's update directory could not be created as an exception occured.");
                throw new IOException(string.Format(strings.MainFolderCreationExceptionText,
                    ex.Message));
            }
        }

        /// <summary>
        ///     Determines whether the specified update package is valid, or not.
        /// </summary>
        /// <returns>A <see cref="VerificationResult" /> containing information about the validation of all packages.</returns>
        /// <exception cref="FileNotFoundException">One of the specified update packages could not be found.</exception>
        /// <exception cref="ArgumentException">The signature of an update package is <c>null</c> or empty.</exception>
        /// <seealso cref="RemoveLocalPackage" />
        /// <seealso cref="VerifyUpdates(UpdateCheckResult)" />
        /// <seealso cref="VerifyUpdates(IEnumerable{UpdatePackage})" />
        public Task<bool> VerifyUpdate(UpdatePackage package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            return Task.Run(() =>
            {
                if (package.Signature == null || package.Signature.Length <= 0)
                    throw new ArgumentException(
                        $"The signature of version \"{package.Version}\" is empty and thus invalid.");
                // TODO: Localize

                var packagePath = GetLocalPackagePath(package);
                if (!File.Exists(packagePath))
                    throw new FileNotFoundException(string.Format(strings.PackageFileNotFoundExceptionText,
                        package.Version));

                // TODO: Check if packages will be disposed properly
                using (var stream = File.Open(packagePath, FileMode.Open))
                {
                    try
                    {
                        Logger.Info($"Verifying the signature for the package of version {package.Version}.");
                        using (var rsa = new RsaManager(PublicKey))
                        {
                            return rsa.VerifyData(stream, Convert.FromBase64String(package.Signature));
                        }
                    }
                    catch
                    {
                        // Don't throw an exception.
                        Logger.Warn($"Invalid signature found for the package of version {package.Version}.");
                        return false;
                    }
                }
            });
        }

        /// <summary>
        ///     Determines whether all specified update packages are valid, or not.
        /// </summary>
        /// <returns>A <see cref="VerificationResult" /> containing information about the validation of all packages.</returns>
        /// <exception cref="FileNotFoundException">One of the specified update packages could not be found.</exception>
        /// <exception cref="ArgumentException">The signature of an update package is <c>null</c> or empty.</exception>
        /// <seealso cref="RemoveLocalPackage" />
        /// <seealso cref="VerifyUpdate" />
        public async Task<VerificationResult> VerifyUpdates(IEnumerable<UpdatePackage> packages)
        {
            if (packages == null)
                throw new ArgumentNullException(nameof(packages));

            var updatePackages = packages.ToArray();
            if (updatePackages.Length == 0)
                return default;

            var validationResult = new VerificationResult(updatePackages.Length);
            await updatePackages.ForEachAsync(async p =>
            {
                if (!await VerifyUpdate(p))
                    validationResult.InvalidPackages.Add(p);
            });
            return validationResult;
        }
    }
}