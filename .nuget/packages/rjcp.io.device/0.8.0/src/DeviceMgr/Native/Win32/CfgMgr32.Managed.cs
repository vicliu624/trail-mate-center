namespace RJCP.Native.Win32
{
    using System;

#if !NETFRAMEWORK
    using System.Buffers;
#endif

    internal static partial class CfgMgr32
    {
        // P/Invoke methods specific for .NET Framework

        // Don't use StringBuilder, as it's slow [CA1838]. Allocate either on the stack, or on the heap.
        //
        // [CA1838] https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1838

#if NETFRAMEWORK
        private static readonly string[] EmptyString = new string[0];
#else
        private static readonly string[] EmptyString = Array.Empty<string>();
#endif

        public static CONFIGRET CM_Get_DevNode_Registry_Property(SafeDevInst devInst, CM_DRP property, out int dataType, out int value)
        {
            int length = 4;
            CONFIGRET ret = CM_Get_DevNode_Registry_Property(devInst, property, out dataType, out value, ref length, 0);
            if (ret != CONFIGRET.CR_SUCCESS) return ret;

            Kernel32.REG_DATATYPE regDataType = (Kernel32.REG_DATATYPE)dataType;
            if (regDataType != Kernel32.REG_DATATYPE.REG_DWORD) return CONFIGRET.CR_UNEXPECTED_TYPE;

            return ret;
        }

        public static unsafe CONFIGRET CM_Get_DevNode_Registry_Property(SafeDevInst devInst, CM_DRP property, out int dataType, out string buffer)
        {
            int length = 0;
            CONFIGRET ret = CM_Get_DevNode_Registry_Property(devInst, property, out dataType, IntPtr.Zero, ref length, 0);
            if (ret != CONFIGRET.CR_SUCCESS && ret != CONFIGRET.CR_BUFFER_SMALL) {
                buffer = string.Empty;
                return ret;
            }

            if (length <= 0) {
                buffer = string.Empty;
                return CONFIGRET.CR_UNEXPECTED_LENGTH;
            }

            Kernel32.REG_DATATYPE regDataType = (Kernel32.REG_DATATYPE)dataType;
            if (regDataType != Kernel32.REG_DATATYPE.REG_SZ) {
                buffer = string.Empty;
                return CONFIGRET.CR_UNEXPECTED_TYPE;
            }

            if (length % 2 == 1) length++;
            int bloblen = length / 2;

            if (length <= MaxLengthStack) {
                char* blobptr = stackalloc char[bloblen];
                ret = CM_Get_DevNode_Registry_Property(devInst, property, out _, blobptr, ref length, 0);
                if (ret != CONFIGRET.CR_SUCCESS) {
                    buffer = string.Empty;
                    return ret;
                }

                // Subtract one for the NUL at the end.
                if (blobptr[bloblen - 1] == (char)0) bloblen--;
                buffer = new string(blobptr, 0, bloblen);
            } else {
#if NETFRAMEWORK
                char[] blob = new char[bloblen];
#else
                char[] blob = ArrayPool<char>.Shared.Rent(bloblen);
                try {
#endif
                fixed (char* blobptr = &blob[0]) {
                    ret = CM_Get_DevNode_Registry_Property(devInst, property, out _, blobptr, ref length, 0);
                    if (ret != CONFIGRET.CR_SUCCESS) {
                        buffer = string.Empty;
                        return ret;
                    }

                    // Subtract one for the NUL at the end.
                    if (blobptr[bloblen - 1] == (char)0) bloblen--;
                    buffer = new string(blobptr, 0, bloblen);
                }
#if !NETFRAMEWORK
                } finally {
                    ArrayPool<char>.Shared.Return(blob);
                }
#endif
            }
            return ret;
        }

        public static unsafe CONFIGRET CM_Get_DevNode_Registry_Property(SafeDevInst devInst, CM_DRP property, out int dataType, out string[] buffer)
        {
            int length = 0;
            CONFIGRET ret = CM_Get_DevNode_Registry_Property(devInst, property, out dataType, IntPtr.Zero, ref length, 0);
            if (ret != CONFIGRET.CR_SUCCESS && ret != CONFIGRET.CR_BUFFER_SMALL) {
                buffer = EmptyString;
                return ret;
            }

            if (length <= 0) {
                buffer = EmptyString;
                return CONFIGRET.CR_UNEXPECTED_LENGTH;
            }

            Kernel32.REG_DATATYPE regDataType = (Kernel32.REG_DATATYPE)dataType;
            if (regDataType != Kernel32.REG_DATATYPE.REG_MULTI_SZ) {
                buffer = EmptyString;
                return CONFIGRET.CR_UNEXPECTED_TYPE;
            }

            if (length % 2 == 1) length++;
            int bloblen = length / 2;

            if (length <= MaxLengthStack) {
                char* blobptr = stackalloc char[bloblen];
                ret = CM_Get_DevNode_Registry_Property(devInst, property, out _, blobptr, ref length, 0);
                if (ret != CONFIGRET.CR_SUCCESS) {
                    buffer = EmptyString;
                    return ret;
                }
                buffer = Marshalling.GetMultiSz(blobptr, bloblen).ToArray();
            } else {
#if NETFRAMEWORK
                char[] blob = new char[bloblen];
#else
                char[] blob = ArrayPool<char>.Shared.Rent(bloblen);
                try {
#endif
                fixed (char* blobptr = &blob[0]) {
                    ret = CM_Get_DevNode_Registry_Property(devInst, property, out _, blobptr, ref length, 0);
                    if (ret != CONFIGRET.CR_SUCCESS) {
                        buffer = EmptyString;
                        return ret;
                    }
                    buffer = Marshalling.GetMultiSz(blobptr, bloblen).ToArray();
                }
#if !NETFRAMEWORK
                } finally {
                    ArrayPool<char>.Shared.Return(blob);
                }
#endif
            }
            return ret;
        }

        public static unsafe CONFIGRET CM_Get_Device_ID_List(string filter, out string[] buffer)
        {
            // We don't use the stack, as the list is usually quite long.

            CONFIGRET ret = CM_Get_Device_ID_List_Size(out int length, filter, 0);
            if (ret != CONFIGRET.CR_SUCCESS) {
                buffer = EmptyString;
                return ret;
            }

#if NETFRAMEWORK
            char[] blob = new char[length];
#else
            char[] blob = ArrayPool<char>.Shared.Rent(length);
            try {
#endif
            fixed (char* blobptr = &blob[0]) {
                ret = CM_Get_Device_ID_List(filter, blobptr, length, 0);
                if (ret != CONFIGRET.CR_SUCCESS) {
                    buffer = EmptyString;
                    return ret;
                }
                buffer = Marshalling.GetMultiSz(blobptr, length).ToArray();
            }
#if !NETFRAMEWORK
            } finally {
                ArrayPool<char>.Shared.Return(blob);
            }
#endif
            return ret;
        }
    }
}
