using System;
using System.Runtime.InteropServices;

namespace com.epson.label.driver
{
    //------------------------------------
    //Definitions Printer StateCode for CW-80xx.
    //Please add StatusCode which are not being defined.
    //Some value that is not being defined in enum by specification change of SDK may be returned. 
    //Refer SDK specifications for the latest definition.
    //------------------------------------
    public enum StatusCode
    {
        ST_Error        = 0x00,
        ST_TestPrint    = 0x01,
        ST_Busy         = 0x02,
        ST_Wait         = 0x03,
        ST_Idle         = 0x04,
        ST_NotPrint     = 0x05,
        ST_Cleaning     = 0x07,
    }

    //------------------------------------
    //Definitions Printer ErrorCode for CW-80xx.
    //Please add ErrorCode which are not being defined.
    //Some value that is not being defined in enum by specification change of SDK may be returned. 
    //Refer SDK specifications for the latest definition.
    //------------------------------------
    public enum ErrorCode
    {
        ERR_FatalError  = 0x00,
        ERR_CoverOpen   = 0x02,
        ERR_PaperJam    = 0x04,
        ERR_Inkout      = 0x05,
        ERR_Paperout    = 0x06,
    }

    //------------------------------------
    //Definitions Printer WarningCode for CW-80xx.
    //Please add WarningCode which are not being defined.
    //Some value that is not being defined in enum by specification change of SDK may be returned. 
    //Refer SDK specifications for the latest definition.
    //------------------------------------
    public enum WarningCode
    {
        WAR_InkLow_B = 0x10,
        WAR_InkLow_C = 0x11,
        WAR_InkLow_M = 0x12,
        WAR_InkLow_Y = 0x13,
    }

    /// <summary>
    /// This is the class that defines the flag of warning code.
    /// </summary>
    public class WARNING_INFO
    {
        public bool InkLowWarning = false;
        public bool OtherWarning = false;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct STATUSVERSION
    {
        public short MajorVersion;
        public short MinerVersion;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct LABELCARTRIDGESTATUS
    {
        public byte CartridgeType;
        public int ColorType;
        public byte CartridgeRemain;
        public byte InkDimension;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct LABELROMCODEINFO
    {
        public byte Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        public byte[] Data;
    }

    class ENSStatus : ENS2
    {
        #region Structs

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        public struct ENSTATUS_LABEL
        {
            public int StatusSize;
            public STATUSVERSION Version;
            public byte StatusCode;
            public byte ErrorCode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] WarningCode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public LABELCARTRIDGESTATUS[] CartridgeStatus;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] InkRemainInfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public byte[] SerialNo;
            public int ReplaceCartridge;
            public LABELROMCODEINFO RomCode;
            public byte PaperPathType;
            public byte PaperRestUnit;
            public int PaperRestValue;
            public byte InkSelect;
            public int RollRest;
            public byte FatalErrorCodeSetFlag;
            public byte FatalErrorCode;
            public byte PrintStopPause;
            public byte PrintStopCoverOpen;
            public byte PrintStopWaitPanel;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] Reserved;
        };

        #endregion

        #region Fields

        private bool _disposed = false;
        public ENSTATUS_LABEL _struct;

        #endregion

        #region Property

        public byte StatusCode
        {
            get
            {
                return _struct.StatusCode;
            }
        }
        public byte ErrorCode
        {
            get
            {
                return _struct.ErrorCode;
            }
        }
        public byte[] WarningCode
        {
            get
            {
                return _struct.WarningCode;
            }
        }

        #endregion

        #region Constructors/Destructors

        public ENSStatus()
        {
            _ptrToStruct = Marshal.AllocCoTaskMem(Marshal.SizeOf(_struct));

            _struct = new ENSTATUS_LABEL();
            _struct.StatusSize = Marshal.SizeOf(_struct);
            StructureToPtr();
            PtrToStructure();
        }

        #endregion

        #region Methods

        public override void PtrToStructure()
        {
            _struct = (ENSTATUS_LABEL)Marshal.PtrToStructure(_ptrToStruct, typeof(ENSTATUS_LABEL));
        }

        public override void StructureToPtr()
        {
            Marshal.StructureToPtr(_struct, _ptrToStruct, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Release managed resources.
                }

                // Release unmanaged resources.

                _disposed = true;
            }
            base.Dispose(disposing);
        }

        #endregion


    }
}
