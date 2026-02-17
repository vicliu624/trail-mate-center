namespace RJCP.Native.Win32
{
    using System;
    using System.Runtime.ConstrainedExecution;
    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// Gets a handle to the CfgMgr32 DevInst object.
    /// </summary>
    public class SafeDevInst : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SafeDevInst"/> class.
        /// </summary>
        public SafeDevInst() : base(true) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SafeDevInst"/> class.
        /// </summary>
        /// <param name="newHandle">The handle to assign to this object.</param>
        public SafeDevInst(IntPtr newHandle) : base(true)
        {
            handle = newHandle;
        }

        /// <summary>
        /// Executes the code required to free the handle.
        /// </summary>
        /// <returns>
        /// Returns <see langword="true"/> if the handle is released successfully; otherwise, in the event of a
        /// catastrophic failure, <see langword="false"/>. In this case, it generates a releaseHandleFailed MDA Managed
        /// Debugging Assistant.
        /// </returns>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            // Here, we must obey all rules for constrained execution regions.

            // If ReleaseHandle failed, it can be reported via the "releaseHandleFailed" managed debugging assistant
            // (MDA). This MDA is disabled by default, but can be enabled in a debugger or during testing to diagnose
            // handle corruption problems. We do not throw an exception because most code could not recover from the
            // problem.

            // There is no documentation that this handle should be closed. When trying to close through single stepping
            // with a debugger, we get an exception on Windows 10. Further, we see that when querying the root multiple
            // times, the actual value of the handle is constant, thus suggesting that the implementation doesn't need
            // to be freed.

            // So in effect, we use this class to wrap the result of the Win32 API in a type safe manner, but don't need
            // the atomic / clean disposal of the object.

            //return Kernel32.CloseHandle(handle);

            // Also, please note, that it is expected that closing this handle has no effect, as when we return a new
            // handle via DeviceInstance.Handle, we copy it, so if the user closes that copy (which has no effect), it
            // won't affect us here.
            SetHandleAsInvalid();
            return true;
        }
    }
}
