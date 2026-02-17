namespace RJCP.Core.Environment
{
    using System;
    using System.IO;
    using Resources;

    public static partial class Xdg
    {
        private sealed class XdgWindows : IXdgResolver
        {
            public string ResolveHomeDirectory()
            {
                string winHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(winHome)) winHome = Environment.GetEnvironmentVariable("USERPROFILE");
                if (string.IsNullOrEmpty(winHome)) {
                    string winHomeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
                    string winHomePath = Environment.GetEnvironmentVariable("HOMEPATH");
                    if (string.IsNullOrEmpty(winHomeDrive) || string.IsNullOrEmpty(winHomePath)) {
                        throw new InvalidOperationException(Messages.InvalidOperationEx_XdgUnknownHome);
                    }
                    return Path.Combine(winHomeDrive, winHomePath);
                } else {
                    return winHome;
                }
            }

            public string ResolveDirsPath(string environmentVariable, string defaultValue)
            {
                return null;
            }
        }
    }
}
