using System;

namespace com.epson.label.driver
{
    [Serializable]
    class ENSException : Exception
    {
        #region Property

        public int ErrCode { get; set; }

        #endregion

        #region Methods

        public ENSException(int e) : base($"EpsonNet SDK Error: {e} ({GetErrorName(e)})")
        {
            ErrCode = e;
        }

        /// <summary>
        /// Maps a raw EpsonNet SDK error code integer to a human-readable name.
        /// Codes are defined in EpsonNetSDKv2.ERR constants.
        /// </summary>
        private static string GetErrorName(int code)
        {
            return code switch
            {
                0 => "BASE (Success)",
                -1 => "PARAMETER (Invalid parameter)",
                -2 => "INITIALIZE (Initialization failed)",
                -3 => "NOTSUPPORT (Feature not supported)",
                -4 => "PRINTER (Printer error)",
                -5 => "NOTFOUND (Printer not found)",
                -6 => "BUFFERSIZE (Buffer too small)",
                -7 => "TEMPORARY (Temporary error)",
                -8 => "COMMUNICATION (Communication error — check cable/USB connection)",
                -9 => "INVALIDDATA (Invalid data received)",
                -10 => "CHANNEL (Channel error)",
                -11 => "HANDLE (Invalid handle)",
                -12 => "BUSY (Printer busy)",
                -13 => "LOADDLL (DLL load failed)",
                -14 => "DEVICEID (Device ID error)",
                -15 => "PRNHANDLE (Printer handle error)",
                -16 => "PORT (Port error)",
                -17 => "TIMEOUT (Operation timed out)",
                -18 => "CMDREPLY (Command reply error)",
                -19 => "CMDVALUE (Command value error)",
                -20 => "CMDNOTSUPPORT (Command not supported)",
                -21 => "INVALIDHANDLE (Invalid handle)",
                -22 => "USERCANCEL (User cancelled)",
                -23 => "THREADSTART (Thread start error)",
                -24 => "TRAPRESOURCE (Trap resource error)",
                -27 => "TRAPNOTSUPPORT (Trap not supported)",
                -28 => "SETTRAP (Set trap error)",
                -29 => "NOTRAPDATA (No trap data)",
                -30 => "INTERNALMODULE (Internal module error)",
                -1000 => "OTHER (Unknown error)",
                _ => "UNKNOWN"
            };
        }

        #endregion
    }
}
