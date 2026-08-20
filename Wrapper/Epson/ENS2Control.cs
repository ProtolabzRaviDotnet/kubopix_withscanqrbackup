using System;
using System.Runtime.InteropServices;

namespace com.epson.label.driver
{
    /// <summary>
    /// This is the class that controls of EpsonNetSDK modules.
    /// </summary>
    class ENS2Control
    {
        #region Fields

        private static ENS2Control _singleton = new ENS2Control();
        private String _devName = "";
        private String _portName = "";
        private IntPtr _ptrHandle = IntPtr.Zero;
        private static readonly object _lockObject = new object();

        #endregion

        #region Constants

        private const String INFO_COMMAND = "STATUS_LABEL";

        #endregion

        #region Constructors/Destructors

        private ENS2Control()
        {
        }

        #endregion

        #region Methods

        public static ENS2Control GetInstance()
        {
            return _singleton;
        }

        /// <summary>
        /// This is the method that implement ENSInitialize.
        /// </summary>
        /// <param name="devName">Device name of the selected printer.</param>
        /// <param name="portName">Port name of the selected printer.</param>
        public void Initialize(String devName, String portName)
        {
            System.Threading.Monitor.Enter(_lockObject);
            try
            {
                _devName = devName;
                _portName = portName;

                int errCode = EpsonNetSDKv2.API.ENSInitialize_v2(null, IntPtr.Zero);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }
            }
            catch (Exception)
            {
                System.Threading.Monitor.Exit(_lockObject);
                throw;
            }
        }
        /// Retrieves ink levels formatted as a string.

        /// </summary>

        public string GetInkRemaining()

        {

            try

            {

                ENSStatus.ENSTATUS_LABEL structSTATUS;

                GetPrinterStatus(out structSTATUS);



                System.Collections.Generic.List<string> levels = new System.Collections.Generic.List<string>();

                if (structSTATUS.CartridgeStatus != null)

                {

                    foreach (var cartridge in structSTATUS.CartridgeStatus)

                    {

                        // Filter out unused/empty slots (where CartridgeRemain is 255, CartridgeType is 255/0, or ColorType is -1)

                        if (cartridge.CartridgeType != 0 &&

                            cartridge.CartridgeType != 255 &&

                            cartridge.ColorType != -1 &&

                            cartridge.CartridgeRemain >= 0 &&

                            cartridge.CartridgeRemain <= 100)

                        {

                            string colorName = GetColorName(cartridge.ColorType);

                            string levelStr;



                            // Interpret remaining index code (e.g. 1 = OK, 2 = Low, 3 = Empty) 

                            // to avoid displaying confusing "1%" for full ink.

                            if (cartridge.CartridgeRemain == 1)

                            {

                                levelStr = "OK";

                            }

                            else if (cartridge.CartridgeRemain == 2)

                            {

                                levelStr = "Low";

                            }

                            else if (cartridge.CartridgeRemain == 3)

                            {

                                levelStr = "Empty";

                            }

                            else

                            {

                                levelStr = $"{cartridge.CartridgeRemain}%";

                            }



                            levels.Add($"{colorName}: {levelStr}");

                        }

                    }

                }



                if (levels.Count > 0)

                {

                    return string.Join(", ", levels);

                }



                if (structSTATUS.InkRemainInfo != null)

                {

                    System.Collections.Generic.List<string> rawRemaining = new System.Collections.Generic.List<string>();

                    for (int i = 0; i < structSTATUS.InkRemainInfo.Length; i++)

                    {

                        byte remain = structSTATUS.InkRemainInfo[i];

                        if (remain != 0 && remain != 255 && remain <= 100)

                        {

                            string levelStr;

                            if (remain == 1)

                                levelStr = "OK";

                            else if (remain == 2)

                                levelStr = "Low";

                            else if (remain == 3)

                                levelStr = "Empty";

                            else

                                levelStr = $"{remain}%";



                            rawRemaining.Add($"Slot {i + 1}: {levelStr}");

                        }

                    }

                    if (rawRemaining.Count > 0)

                    {

                        return string.Join(", ", rawRemaining);

                    }

                }



                return "Unknown";

            }

            catch (Exception ex)

            {

                return $"Error: {ex.Message}";

            }

        }



        private string GetColorName(int colorType)

        {

            switch (colorType)

            {

                case 1: return "Black";

                case 2: return "Cyan";

                case 3: return "Magenta";

                case 4: return "Yellow";

                case 8: return "Magenta"; // ColorType 8 represents Magenta (bitmask flag 0x08)

                default: return $"Color {colorType}";

            }

        }



        /// <summary>
        /// <summary>
        /// This is the method that get the staus of selected printer.
        /// </summary>
        /// <param name="structSTATUS">ENSTATUS_LABEL structure object.(out)</param>
        public void GetPrinterStatus(out ENSStatus.ENSTATUS_LABEL structSTATUS)
        {
            IntPtr ptrInt = IntPtr.Zero;
            IntPtr ptrTemp = IntPtr.Zero;
            IntPtr ptrGetBuff = IntPtr.Zero;

            try
            {
                // Handling of to call ENSGetDeviceID_v2.
                int buffLen = 0;

                // ENSGetDeviceID 1st call. Get the required buffLen.
                int errCode = EpsonNetSDKv2.API.ENSGetDeviceID_v2((int)EpsonNetSDKv2.PATHTYPE.PRINTER,
                                                                  _devName, IntPtr.Zero, ref buffLen, 0x1);
                if (errCode != EpsonNetSDKv2.ERR.BUFFERSIZE)
                {
                    throw new ENSException(errCode);
                }

                ptrTemp = Marshal.AllocCoTaskMem(buffLen);

                // ENSGetDeviceID 2nd call.
                errCode = EpsonNetSDKv2.API.ENSGetDeviceID_v2((int)EpsonNetSDKv2.PATHTYPE.PRINTER,
                                                              _devName, ptrTemp, ref buffLen, 0x1);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }

                // Handling of to call ENSOpenCommunication_v2.
                errCode = EpsonNetSDKv2.API.ENSOpenCommunication_v2((int)EpsonNetSDKv2.PATHTYPE.PRINTER,
                                                                  _devName, ptrTemp, out _ptrHandle, IntPtr.Zero);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }

                // Get printer status information.
                structSTATUS = new ENSStatus.ENSTATUS_LABEL();


                // Handling of to call ENSGetInformation.
                buffLen = 0;

                // ENSGetInformation 1st call. Get the required buffLen.
                errCode = EpsonNetSDKv2.API.ENSGetInformation_v2(_ptrHandle, INFO_COMMAND, IntPtr.Zero, IntPtr.Zero, ref buffLen);
                if (errCode != EpsonNetSDKv2.ERR.BUFFERSIZE)
                {
                    throw new ENSException(errCode);
                }
                
                ptrGetBuff = Marshal.AllocCoTaskMem(buffLen);

                // ENSGetInformation 2nd call.
                errCode = EpsonNetSDKv2.API.ENSGetInformation_v2(_ptrHandle, INFO_COMMAND, IntPtr.Zero, ptrGetBuff, ref buffLen);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }

                // Get ENSSTATUS structure object. 
                structSTATUS = (ENSStatus.ENSTATUS_LABEL)Marshal.PtrToStructure(ptrGetBuff, typeof(ENSStatus.ENSTATUS_LABEL));

                // Handling of to call ENSCloseCommunication.
                errCode = EpsonNetSDKv2.API.ENSCloseCommunication_v2(_ptrHandle);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }
            }
            catch (Exception)
            {
                // Error handling.
                throw;
            }
            finally
            {
                if (ptrInt != IntPtr.Zero) Marshal.FreeHGlobal(ptrInt);
                if (ptrTemp != IntPtr.Zero) Marshal.FreeCoTaskMem(ptrTemp);
                if (ptrGetBuff != IntPtr.Zero) Marshal.FreeCoTaskMem(ptrGetBuff);
            }
        }

        /// <summary>
        /// This is the method that get the StatusCode.
        /// </summary>
        /// <returns>StatusCode information.</returns>
        public StatusCode GetStatusInformation()
        {  
            try
            {
                ENSStatus.ENSTATUS_LABEL structSTATUS = new ENSStatus.ENSTATUS_LABEL();
                GetPrinterStatus(out structSTATUS);
                StatusCode sc = (StatusCode)structSTATUS.StatusCode;
                return sc;
            }
            catch (Exception)
            {
                // Error handling.
                throw;
            }
        }

        /// <summary>
        /// This is the method that get the ErrorCode.
        /// </summary>
        /// <returns>ErrorCode information.</returns>
        public ErrorCode GetErrorInformation()
        {
            try
            {
                ENSStatus.ENSTATUS_LABEL structSTATUS = new ENSStatus.ENSTATUS_LABEL();
                GetPrinterStatus(out structSTATUS);

                if (structSTATUS.PrintStopCoverOpen != 0 || structSTATUS.ErrorCode == (byte)ErrorCode.ERR_CoverOpen)
                {
                    return ErrorCode.ERR_CoverOpen;
                }

                ErrorCode ec = (ErrorCode)structSTATUS.ErrorCode;
                return ec;
            }
            catch (Exception)
            {
                // Error handling.
                throw;
            }
        }


        /// <summary>
        /// This is the method that get the WarningCode.
        /// Judge whether inklow warning and other warning occur.
        /// </summary>
        /// <returns>WARNING_INFO class object</returns>
        public WARNING_INFO GetWarningInformation()
        {
            try
            {
                ENSStatus.ENSTATUS_LABEL structSTATUS = new ENSStatus.ENSTATUS_LABEL();
                GetPrinterStatus(out structSTATUS);

                WARNING_INFO warningInfo = new WARNING_INFO();

                for (int i = 0; i < 16; i++)
                {
                    if ((structSTATUS.WarningCode[i] == (int)WarningCode.WAR_InkLow_B) ||
                        (structSTATUS.WarningCode[i] == (int)WarningCode.WAR_InkLow_C) ||
                        (structSTATUS.WarningCode[i] == (int)WarningCode.WAR_InkLow_M) ||
                        (structSTATUS.WarningCode[i] == (int)WarningCode.WAR_InkLow_Y))
                    {
                        warningInfo.InkLowWarning = true;
                    }
                    else if (structSTATUS.WarningCode[i] != 0 && structSTATUS.WarningCode[i] != 255)
                    {
                        warningInfo.OtherWarning = true;
                    }
                }
                return warningInfo;
            }
            catch (Exception)
            {
                // Error handling.
                throw;
            }
        }

        /// <summary>
        /// Retrieves the Device ID (MFG, MDL, CMD, CLS) from the EpsonNet SDK DLL and returns a simplified printer model name.
        /// </summary>
        public string GetDeviceID()
        {
            IntPtr ptrTemp = IntPtr.Zero;
            try
            {
                int buffLen = 0;
                int errCode = EpsonNetSDKv2.API.ENSGetDeviceID_v2((int)EpsonNetSDKv2.PATHTYPE.PRINTER,
                                                                  _devName, IntPtr.Zero, ref buffLen, 0x1);
                if (errCode != EpsonNetSDKv2.ERR.BUFFERSIZE)
                {
                    throw new ENSException(errCode);
                }

                ptrTemp = Marshal.AllocCoTaskMem(buffLen);

                errCode = EpsonNetSDKv2.API.ENSGetDeviceID_v2((int)EpsonNetSDKv2.PATHTYPE.PRINTER,
                                                              _devName, ptrTemp, ref buffLen, 0x1);
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }

                EpsonNetSDKv2.V2DEVICEID01 devId = (EpsonNetSDKv2.V2DEVICEID01)Marshal.PtrToStructure(ptrTemp, typeof(EpsonNetSDKv2.V2DEVICEID01));
                string mfg = devId.MFG != null ? new string(devId.MFG).Split('\0')[0].Trim() : "";
                string mdl = devId.MDL != null ? new string(devId.MDL).Split('\0')[0].Trim() : "";

                if (!string.IsNullOrEmpty(mfg) && !string.IsNullOrEmpty(mdl))
                {
                    if (mdl.StartsWith(mfg, StringComparison.OrdinalIgnoreCase))
                    {
                        return mdl;
                    }
                    return $"{mfg} {mdl}";
                }
                else if (!string.IsNullOrEmpty(mdl))
                {
                    return mdl;
                }
                else if (!string.IsNullOrEmpty(mfg))
                {
                    return mfg;
                }
                return "N/A";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (ptrTemp != IntPtr.Zero) Marshal.FreeCoTaskMem(ptrTemp);
            }
        }

        /// <summary>
        /// Retrieves the Serial Number from the EpsonNet SDK DLL status structure.
        /// </summary>
        public string GetSerialNumber()
        {
            try
            {
                ENSStatus.ENSTATUS_LABEL structSTATUS;
                GetPrinterStatus(out structSTATUS);
                if (structSTATUS.SerialNo != null)
                {
                    string serial = System.Text.Encoding.ASCII.GetString(structSTATUS.SerialNo).Split('\0')[0].Trim();
                    return string.IsNullOrEmpty(serial) ? "N/A" : serial;
                }
                return "N/A";
            }
            catch (Exception)
            {
                return "N/A";
            }
        }

        /// <summary>
        /// This is the method that implement ENSRelease.
        /// </summary>
        public void Release()
        {
            try
            {
                int errCode = EpsonNetSDKv2.API.ENSRelease_v2();
                if (errCode != EpsonNetSDKv2.ERR.BASE)
                {
                    throw new ENSException(errCode);
                }
            }

            catch (Exception)
            {
                // Error handling.
                throw;
            }
            finally

            {

                System.Threading.Monitor.Exit(_lockObject);

            }
        }

        #endregion
    }
}
