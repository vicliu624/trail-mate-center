namespace RJCP.Core.Environment
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Resources;

    /// <summary>
    /// A class to implement portions of the XDG Base Directory Specification.
    /// </summary>
    /// <remarks>
    /// The XDG specification is for Unix like operating systems. Please refer to
    /// https://specifications.freedesktop.org/basedir-spec/basedir-spec-0.7.html
    /// </remarks>
    public static partial class Xdg
    {
        private static readonly object s_XdgResolverSyncRoot = new object();
        private static IXdgResolver s_XdgResolver;

        private static IXdgResolver XdgResolver
        {
            get
            {
                if (s_XdgResolver == null) {
                    lock (s_XdgResolverSyncRoot) {
                        if (s_XdgResolver == null) {
                            if (Platform.IsUnix()) {
                                s_XdgResolver = new XdgUnix();
                            } else if (Platform.IsWinNT()) {
                                s_XdgResolver = new XdgWindows();
                            } else {
                                s_XdgResolver = new XdgUnknown();
                            }
                        }
                    }
                }
                return s_XdgResolver;
            }
        }

        /// <summary>
        /// Gets the folder path for a specific <see cref="SpecialFolder"/>.
        /// </summary>
        /// <param name="folder">The special folder value to obtain.</param>
        /// <returns>A path in the native format.</returns>
        /// <exception cref="ArgumentException">The value of <paramref name="folder"/> is not known.</exception>
        /// <exception cref="System.Security.SecurityException">
        /// The caller doesn't have permissions to read environment variables.
        /// </exception>
        /// <exception cref="PlatformNotSupportedException">
        /// The platform is not supported. Only Windows NT and Unix platforms are supported.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Unable to determine the location of the home directory.
        /// </exception>
        /// <remarks>
        /// This function fulfills the XDG Base Specification v0.7 and is intended for usage only on Unix like operating
        /// systems. Environment variables defined in the XDG specification are for Unix like operating systems and are
        /// interpreted as such. No variable substitution is done within the environment variables from the XDG
        /// specification and must be expanded prior.
        /// <para>
        /// When determining the home directory, the <c>$HOME</c> is used on Unix like operating systems. If the
        /// <c>$HOME</c> environment variable is not defined, the root directory path '/' is used. On Windows NT
        /// operating systems, the local application directory from the system is returned (as referred to
        /// <see cref="Environment.SpecialFolder"/> enumeration).
        /// </para>
        /// </remarks>
        public static string GetFolderPath(SpecialFolder folder)
        {
            return GetFolderPathInternal(folder).ToString();
        }

        private static string ResolvePathEnvironmentVariable(string variable, string defaultPath)
        {
            string environmentVariable = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(environmentVariable)) {
                if (Path.IsPathRooted(defaultPath)) return defaultPath;
                return Path.Combine(XdgResolver.ResolveHomeDirectory(), defaultPath);
            }

            return Environment.GetEnvironmentVariable(variable);
        }

        /// <summary>
        /// Returns the full path to the file name, relative to the special folder provided.
        /// </summary>
        /// <param name="folder">The special folder base directory.</param>
        /// <param name="fileName">Name of the file to resolve.</param>
        /// <returns>A path in the native format.</returns>
        /// <exception cref="PlatformNotSupportedException">The platform is not supported.</exception>
        /// <remarks>
        /// For some folder paths (specifically, only <see cref="SpecialFolder.LocalApplicationData"/> and
        /// <see cref="SpecialFolder.CommonApplicationData"/>, a set of paths may be searched if a file is not found in
        /// the current home directory. This should be the function used when identifying a file for loading from disk.
        /// Use the method <see cref="GetFolderPath(SpecialFolder)"/> for getting a path when writing a file to disk.
        /// </remarks>
        public static string ResolveFile(SpecialFolder folder, string fileName)
        {
            return GetFolderPathInternal(folder, fileName, true);
        }

        /// <summary>
        /// Returns the full path to the directory name, relative to the special folder provided.
        /// </summary>
        /// <param name="folder">The special folder base directory.</param>
        /// <param name="directoryName">Name of the directory to resolve.</param>
        /// <returns>A path in the native format.</returns>
        /// <exception cref="PlatformNotSupportedException">The platform is not supported.</exception>
        /// <remarks>
        /// For some folder paths (specifically, only <see cref="SpecialFolder.LocalApplicationData"/> and
        /// <see cref="SpecialFolder.CommonApplicationData"/>, a set of paths may be searched if a file is not found in
        /// the current home directory. This should be the function used when identifying a file for loading from disk.
        /// Use the method <see cref="GetFolderPath(SpecialFolder)"/> for getting a path when writing a file to disk.
        /// </remarks>
        public static string ResolveDirectory(SpecialFolder folder, string directoryName)
        {
            return GetFolderPathInternal(folder, directoryName, false);
        }

        private static string GetFolderPathInternal(SpecialFolder folder)
        {
            switch (folder) {
            case SpecialFolder.LocalApplicationData:
                return ResolvePathEnvironmentVariable(
                    "XDG_DATA_HOME", Path.Combine(".local", "share"));
            case SpecialFolder.CommonApplicationData:
                return ResolvePathEnvironmentVariable(
                    "XDG_CONFIG_HOME", ".config");
            case SpecialFolder.CacheData:
                return ResolvePathEnvironmentVariable(
                    "XDG_CACHE_HOME", ".cache");
            default:
                throw new ArgumentException(Messages.ArgumentEx_XdgUnknownSpecialFolder, nameof(folder));
            }
        }

        private static string GetFolderPathInternal(SpecialFolder folder, string fileName, bool file)
        {
            string homePath = Path.Combine(GetFolderPathInternal(folder), fileName);
            if (file && File.Exists(homePath)) return homePath;
            if (!file && Directory.Exists(homePath)) return homePath;

            string path;
            switch (folder) {
            case SpecialFolder.LocalApplicationData:
                // XDG_DATA_DIRS
                path = XdgResolver.ResolveDirsPath("XDG_DATA_DIRS", "/usr/local/share:/usr/share");
                break;
            case SpecialFolder.CommonApplicationData:
                // XDG_CONFIG_DIRS
                path = XdgResolver.ResolveDirsPath("XDG_CONFIG_DIRS", "/etc/xdg");
                break;
            default:
                path = null;
                break;
            }

            IEnumerable<string> paths = SplitPath(path);
            foreach (string splitPath in paths) {
                string checkPath = Path.Combine(splitPath, fileName);
                if (file && File.Exists(checkPath)) return checkPath;
                if (!file && Directory.Exists(checkPath)) return checkPath;
            }
            return homePath;
        }

        private static IEnumerable<string> SplitPath(string pathList)
        {
            if (string.IsNullOrWhiteSpace(pathList))
#if NET40
                return new string[0];
#else
                return Array.Empty<string>();
#endif
            return pathList.Split(':');
        }
    }
}
