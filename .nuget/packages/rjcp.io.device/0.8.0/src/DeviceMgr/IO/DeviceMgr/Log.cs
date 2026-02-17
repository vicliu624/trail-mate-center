namespace RJCP.IO.DeviceMgr
{
    using Diagnostics.Trace;

    internal static class Log
    {
        private const string CfgMgrIdentifier = "RJCP.IO.DeviceMgr";

        public static readonly LogSource CfgMgr = new LogSource(CfgMgrIdentifier);
    }
}
