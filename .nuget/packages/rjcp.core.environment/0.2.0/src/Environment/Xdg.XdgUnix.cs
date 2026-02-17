namespace RJCP.Core.Environment
{
    using System;

    public static partial class Xdg
    {
        private sealed class XdgUnix : IXdgResolver
        {
            public string ResolveHomeDirectory()
            {
                string unixHome = Environment.GetEnvironmentVariable("HOME");
                return unixHome ?? "/";
            }

            public string ResolveDirsPath(string environmentVariable, string defaultValue)
            {
                string environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
                return environmentValue ?? defaultValue;
            }
        }
    }
}
