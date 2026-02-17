namespace RJCP.Core.Environment
{
    public static partial class Xdg
    {
        private interface IXdgResolver
        {
            string ResolveHomeDirectory();

            string ResolveDirsPath(string environmentVariable, string defaultValue);
        }
    }
}
