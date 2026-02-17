namespace RJCP.Core.Environment
{
    using System;

    public static partial class Xdg
    {
        private sealed class XdgUnknown : IXdgResolver
        {
            public string ResolveHomeDirectory()
            {
                throw new PlatformNotSupportedException();
            }

            public string ResolveDirsPath(string environmentVariable, string defaultValue)
            {
                throw new PlatformNotSupportedException();
            }
        }
    }
}
