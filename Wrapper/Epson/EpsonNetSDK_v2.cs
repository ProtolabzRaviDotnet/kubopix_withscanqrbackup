using System;
using System.Text;
using System.Runtime.InteropServices;

namespace EpsonNetSDKv2
{
	class ERR
	{
		public const int BASE 				= 0;

		public const int PARAMETER          = (BASE - 1);
		public const int INITIALIZE         = (BASE - 2);
		public const int NOTSUPPORT         = (BASE - 3);
		public const int PRINTER            = (BASE - 4);
		public const int NOTFOUND           = (BASE - 5);
		public const int BUFFERSIZE         = (BASE - 6);
		public const int TEMPORARY          = (BASE - 7);
		public const int COMMUNICATION      = (BASE - 8);
		public const int INVALIDDATA		= (BASE - 9);
		public const int CHANNEL			= (BASE - 10);
		public const int HANDLE				= (BASE - 11);
		public const int BUSY				= (BASE - 12);
		public const int LOADDLL			= (BASE - 13);
		public const int DEVICEID			= (BASE - 14);
		public const int PRNHANDLE			= (BASE - 15);
		public const int PORT				= (BASE - 16);
		public const int TIMEOUT			= (BASE - 17);
		public const int CMDREPLY			= (BASE - 18);
		public const int CMDVALUE			= (BASE - 19);
		public const int CMDNOTSUPPORT		= (BASE - 20);
		public const int INVALIDHANDLE		= (BASE - 21);
		public const int USERCANCEL         = (BASE - 22);
		public const int THREADSTART        = (BASE - 23);
		public const int TRAPRESOURCE       = (BASE - 24);
		public const int TRAPNOTSUPPORT     = (BASE - 27);
		public const int SETTRAP            = (BASE - 28);
		public const int NOTRAPDATA         = (BASE - 29);
		public const int INTERNALMODULE     = (BASE - 30);
		public const int OTHER				= -1000;


		public const int TYPE_REMOTE			= 1;
		public const int TYPE_UNKNOWN			= 100;

		public const int TYPE_PORT				= 0;
		public const int TYPE_PRINTER			= 1;

		public const int OPT_NOCOMMAND			= 0;
		public const int OPT_COMMUNITYNAME		= 5;
	}

	class TYPE
	{
		public const int REMOTE  		= 1;
		public const int UNKNOWN  		= 100;
	}
	
	class PATHTYPE
	{
		public const int PORT  			= 0;
		public const int PRINTER  		= 1;
	}
	
	class OPT
	{
		public const int NOCOMMAND		= 0;
		public const int COMMUNITYNAME	= 5;
	}

	class DATATYPE
	{
		public const int INTEGER  		= 0x02;
		public const int OCTETSTR  		= 0x04;
		public const int UNKNOWN  		= 0xFE;
		public const int BUFFSIZE  		= 0xFF;
	}

#pragma warning disable 649
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct V2DEVICEID01
	{
		public int		Size;
		public int		Version;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
		public char[]	MFG;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
		public char[]	CMD;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
		public char[]	MDL;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
		public char[]	CLS;
		public int		PrnTypes;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct V2STATUSHEADER
	{
		public uint 	Size;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
		public ushort[]   Version;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct ENSSTATUS
	{
		public uint		Size;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
		public ushort[]	Version;

		public byte		StatusCode;
		public byte		ErrorCode;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		public byte[]	WarningCode2;

		public uint		MonochromePrintedNumber;
		public uint		ColorPrintedNumber;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
		public byte[] SerialNo;

	}

	class OIDSTR
	{
		public const int OID_SIZE =	128;
		public const int COMNAME_SIZE = 64;
	}
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct V2OIDINFO {
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = OIDSTR.OID_SIZE)]
		public char[] 	OID;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = OIDSTR.COMNAME_SIZE)]
		public char[]	Community;
		public int 		DataType;
		public IntPtr	Buffer;
		public int		BuffSize;
	}
#pragma warning restore 649

	class API
	{
		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSInitialize_v2(
										[MarshalAs(UnmanagedType.LPStr)] string? ExDLLPath,
										[Out] IntPtr ExDLLNum);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSRelease_v2();

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSGetDeviceID_v2(
										int PathType,
										[MarshalAs(UnmanagedType.LPStr)] string PrnPath,
										[Out] IntPtr IdBuff,
										ref int BuffLen,
										int StructVersion);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSOpenCommunication_v2(
										int PathType,
										[MarshalAs(UnmanagedType.LPStr)] string PrnPath,
										[In] IntPtr IdBuff,
										out IntPtr PrnHandle,
	 									[In] IntPtr Option);
		
		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSCloseCommunication_v2(IntPtr PrnHandle);
		
		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSGetInformation_v2(
										IntPtr PrnHandle,
										[MarshalAs(UnmanagedType.LPStr)] string Command,
										[In] IntPtr GetParam,
										[Out] IntPtr GetBuff,
										ref int BuffLen);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSSetInformation_v2(
										IntPtr PrnHandle, 
										[MarshalAs(UnmanagedType.LPStr)] string Command,
										[In] IntPtr SetParm, 
										ref int BuffLen);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSStartDiscovery_v2( 
										[MarshalAs(UnmanagedType.LPStr)] string ProtocolType, 
										[MarshalAs(UnmanagedType.LPStr)] string DiscoveryKey,
										int DiscoveryType, 
										int OptionCommand,
										[In] IntPtr OptionData,
										ref int ResultLength);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSGetDiscoveryResult_v2( 
										int DiscoveryHandle,
										int Timeout, 
										ref uint DeviceType,
										StringBuilder ResultBuffer);

		[DllImport("EpsonNetSDKv2.dll")]
		public extern static int ENSTerminateDiscovery_v2(int DiscoveryHandle);
	}
}
