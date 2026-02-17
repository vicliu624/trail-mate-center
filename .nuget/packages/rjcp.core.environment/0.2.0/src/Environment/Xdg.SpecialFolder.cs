namespace RJCP.Core.Environment
{
    public static partial class Xdg
    {
        /// <summary>
        /// Specifies enumerated constants used to retrieve directory paths to system special folders.
        /// </summary>
        public enum SpecialFolder
        {
            /// <summary>
            /// Directory location for local application data (default is <c>$HOME/.local/share</c>).
            /// </summary>
            LocalApplicationData,

            /// <summary>
            /// The common application data, or configuration data (default is <c>$HOME/.config</c>).
            /// </summary>
            CommonApplicationData,

            /// <summary>
            /// Cache data (default is <c>$HOME/.cache</c>).
            /// </summary>
            CacheData
        }
    }
}
