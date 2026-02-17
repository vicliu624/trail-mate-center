namespace RJCP.Core.Environment.Version
{
    /// <summary>
    /// The processor architecture on which the machine is running.
    /// </summary>
    public enum WinArchitecture
    {
        /// <summary>
        /// Unknown.
        /// </summary>
        Unknown,

        /// <summary>
        /// Intel x86 32-bit.
        /// </summary>
        x86,

        /// <summary>
        /// MIPS architecture.
        /// </summary>
        Mips,

        /// <summary>
        /// Alpha architecture.
        /// </summary>
        Alpha,

        /// <summary>
        /// Power PC architecture.
        /// </summary>
        PPC,

        /// <summary>
        /// SHx architecture.
        /// </summary>
        SHX,

        /// <summary>
        /// ARM architecture.
        /// </summary>
        ARM,

        /// <summary>
        /// Intel IA64 architecture (this is not x64).
        /// </summary>
        IA64,

        /// <summary>
        /// Alpha 64-bit architecture.
        /// </summary>
        Alpha64,

        /// <summary>
        /// Microsoft Intermediate Language.
        /// </summary>
        MSIL,

        /// <summary>
        /// AMD64 bit instructions.
        /// </summary>
        x64,

        /// <summary>
        /// ARM 64-bit architecture.
        /// </summary>
        ARM64
    }
}
