using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Ribbon;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace kuboPix.Wrapper
{
    public static class SeaoryPrinterAPI
    {
        private const string DLL_NAME = "SeaorySDK.dll";

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_GetPrinterInfoA(string szPrinterName, int nInfoType, StringBuilder szInfoOut);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_GetPrinterStatusA(string szPrinterName, out uint lpdwStatus);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_GetPrinterWarningA(string szPrinterName, out uint lpdwWarning);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_SetPrinterSettingA(string szPrinterName, byte[] lpDocPropIn);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_ExecCommandA(string szPrinterName, uint dwCommand);

        // --- Info constants ---
        public const int INFO_MODEL_NAME = 1;
        public const int INFO_FW_VERSION = 2;
        public const int INFO_SERIAL_NUMBER = 3;
        public const int INFO_CARD_POSITION = 4;
        public const int INFO_RIBBON_TYPE = 5;
        public const int INFO_RIBBON_COUNT = 6;
        public const int INFO_PRINT_COUNT = 7;
        public const int INFO_REJECT_BOX_STATUS = 11;
        public const int INFO_MAC_ADDRESS = 14;

        // Exec commands
        public const uint CMD_RESET_PRINTER_JAM = 22;

        public static string GetPrinterInfo(string printerName, int infoType)
        {
            StringBuilder buffer = new StringBuilder(512);
            uint result = SOY_PR_GetPrinterInfoA(printerName, infoType, buffer);
            return result == 0 ? buffer.ToString() : $"Error {result}";
        }

        public static (string friendly, string raw) GetPrinterStatus(string printerName)
        {
            uint status;
            uint result = SOY_PR_GetPrinterStatusA(printerName, out status);
            if (result != 0) return ($"SDK Error {result}", $"0x{result:X8}");
            if (status == 0) return ("Ready", "0x00000000");
            string friendly = StatusCodeToMessage(status);
            return (friendly, $"0x{status:X8}");
        }

        public static (string friendly, string raw) GetPrinterWarning(string printerName)
        {
            uint warning;
            uint result = SOY_PR_GetPrinterWarningA(printerName, out warning);
            if (result != 0) return ($"SDK Error {result}", $"0x{result:X8}");
            if (warning == 0) return ("No Warnings", "0x00000000");
            string friendly = WarningCodeToMessage(warning);
            return (friendly, $"0x{warning:X8}");
        }

        //private static string StatusCodeToMessage(uint code)
        //{
        //    switch (code)
        //    {
        //        case 0x00010051: return "Card feed error (51)";
        //        case 0x00010052: return "Card feed error (52)";
        //        case 0x00010053: return "Card feed error (53)";
        //        case 0x0001005E: return "Card eject error";
        //        case 0x0001005F: return "Card out";
        //        case 0x00011014: return "No card in printer";
        //        case 0x00011013: return "Drawing out of card scope";
        //        case 0x00011006: return "Magnetic encoding fail";
        //        default: return $"Error Code 0x{code:X8}";
        //    }
        //}

        private static string StatusCodeToMessage(uint code)
        {
            switch (code)
            {
                case 0x00010001: return "Firmware error - TPH thermistor error";
                case 0x00010002: return "Card feed error (63)";
                case 0x00010003: return "Encoder error (take)";
                case 0x00010004: return "Encoder error (supply)";
                case 0x00010005: return "ADC error";
                case 0x00010006: return "Encoder error (film)";
                case 0x00010007: return "Heater thermistor error";
                case 0x00010008: return "FPGA error";
                case 0x00010009: return "Burn FPGA error";
                case 0x0001000A: return "Burn LCD error";
                case 0x00010010: return "Card jam (10)";
                case 0x00010011: return "Card jam (11)";
                case 0x00010012: return "Card jam (12)";
                case 0x00010013: return "Card jam (13)";
                case 0x00010014: return "Card jam (14)";
                case 0x00010015: return "Card jam (15)";
                case 0x00010016: return "Card jam (16)";
                case 0x00010017: return "Card jam (17)";
                case 0x00010018: return "Card jam (18)";
                case 0x00010019: return "Card jam (19)";
                case 0x0001001A: return "Card jam (1A)";
                case 0x0001001B: return "Card jam (1B)";
                case 0x0001001C: return "Card jam (1C)";
                case 0x0001001D: return "Card jam (1D)";
                case 0x0001001E: return "Card jam (1E)";
                case 0x0001001F: return "Card jam (1F)";
                case 0x00010021: return "Cover open";
                case 0x00010022: return "Rejectbox open";
                case 0x00010023: return "Rejectbox full";
                case 0x00010024: return "Flat cover open";
                case 0x00010031: return "TPH cam error";
                case 0x00010032: return "ADF cam error";
                case 0x00010033: return "Flipper error";
                case 0x00010034: return "Heater cam error";
                case 0x00010035: return "Flat cam error";
                case 0x00010041: return "Ribbon out";
                case 0x00010042: return "Ribbon error";
                case 0x00010043: return "Ribbon missing";
                case 0x00010044: return "Ribbon unsupport";
                case 0x00010045: return "Ribbon missing";
                case 0x00010046: return "Ribbon out";
                case 0x00010047: return "Ribbon mismatch";
                case 0x00010048: return "Ribbon error";
                case 0x00010049: return "Ribbon error";
                case 0x0001004A: return "Ribbon installation error";
                case 0x00010051: return "Card feed error (51)";
                case 0x00010052: return "Card feed error (52)";
                case 0x00010053: return "Card feed error (53)";
                case 0x0001005E: return "Card eject error";
                case 0x0001005F: return "Card out";
                case 0x00010061: return "Authenticate error (61)";
                case 0x00010062: return "Authenticate error (62)";
                case 0x00010063: return "Authenticate error (63)";
                case 0x00010071: return "Film out";
                case 0x00010072: return "Film error (72)";
                case 0x00010073: return "Film missing";
                case 0x00010074: return "Film unsupport";
                case 0x00010075: return "Film missing";
                case 0x00010076: return "Film out";
                case 0x00010077: return "Film error";
                case 0x00010078: return "Film error";
                case 0x00010079: return "Film unsupport";
                case 0x0001007A: return "Film installation error";
                case 0x00010081: return "ADF module error";
                case 0x00010082: return "Indent-printing error";
                case 0x00012001: return "System busy";
                case 0x00012002: return "Invalid command";
                case 0x00012003: return "Parameter error";
                case 0x00012004: return "Validation failed";
                case 0x00012005: return "Read-only data";
                case 0x00012006: return "Card inside";
                case 0x00012007: return "No card inside";
                case 0x00012008: return "Pressing mechanism not at origin";
                case 0x00012009: return "Card input/output timeout";
                case 0x0001200A: return "Ribbon cassette not locked";
                case 0x0001200B: return "Ribbon cassette not detected";
                case 0x0001200C: return "RFID detection failed";
                case 0x0001200D: return "Ribbon tension timeout";
                case 0x0001200E: return "Dial positioning error";
                case 0x0001200F: return "Press down timeout";
                case 0x00012010: return "RFID cardless";
                case 0x00012011: return "RFID key error";
                case 0x00012012: return "Failed to read/write RFID data";
                case 0x00012013: return "Abnormal RFID card";
                case 0x00012014: return "Abnormal down pressure";
                case 0x00012015: return "Abnormal ribbon";
                case 0x00012016: return "Abnormal digital disk";
                case 0x00012017: return "Abnormal card entry";
                case 0x00012018: return "Insufficient Ribbon";
                case 0x000120FD: return "UART error";
                case 0x000120FE: return "Bootloader mode";
                case 0x000120FF: return "Not connected";
                case 0x00010091: return "Card jam (91)";
                case 0x00010092: return "Card jam (92)";
                case 0x000100A1: return "Firmware command not supported";
                case 0x000100A2: return "Firmware command parameter wrong";
                case 0x000100A3: return "Firmware task not created";
                case 0x000100A4: return "Firmware command rejected";
                case 0x000100A5: return "Printer memory full";
                case 0x000100A6: return "Need authentication before processing command";
                case 0x000100A7: return "Fail to update firmware";
                case 0x00011001: return "Track data is empty";
                case 0x00011002: return "Wrong character in track 1";
                case 0x00011003: return "Wrong character in track 2";
                case 0x00011004: return "Wrong character in track 3";
                case 0x00011005: return "Magnetic encoding module not attached";
                case 0x00011006: return "Encoding magnetic stripe fail";
                case 0x00011007: return "Flipper module not attached";
                case 0x00011008: return "600DPI feature not enabled";
                case 0x00011009: return "Track 1 is empty";
                case 0x0001100A: return "Track 2 is empty";
                case 0x0001100B: return "Track 3 is empty";
                case 0x0001100C: return "Track 1, 2 are empty";
                case 0x0001100D: return "Track 1, 3 are empty";
                case 0x0001100E: return "Track 2, 3 are empty";
                case 0x0001100F: return "Track 1, 2, 3 are empty";
                case 0x00011011: return "Ribbon not matched setting";
                case 0x00011012: return "Printing non-standard length card not enabled";
                case 0x00011013: return "Drawing text/image outside card scope";
                case 0x00011014: return "No card in printer";
                case 0x00011015: return "Please take out ribbon";
                case 0x00011020: return "Magstripe is blank";
                case 0x00011021: return "Track 1 data blank or error";
                case 0x00011022: return "Track 2 data blank or error";
                case 0x00011023: return "Track 3 data blank or error";
                case 0x00011024: return "Track 1,2 data blank or error";
                case 0x00011025: return "Track 1,3 data blank or error";
                case 0x00011026: return "Track 2,3 data blank or error";
                case 0x00011027: return "Track 1,2,3 data blank or error";
                case 0x00011030: return "Smart card module not attached";
                case 0x00011031: return "Calibrate smart card position fail";
                case 0x00011040: return "Firmware file does not match printer model";
                case 0x00011041: return "Current firmware same as firmware file";
                case 0x00011042: return "Current firmware newer than firmware file";
                case 0x00011043: return "Indent-printing module not attached";
                case 0x00013001: return "Printer doing ribbon security erase";
                case 0x00013002: return "Printing job suspended";
                case 0x00013003: return "One card in storage position";
                case 0x00013004: return "No card in storage position";
                case 0x00013005: return "Printer is locked";
                case 1167: return "The device is not connected.";
                default: return $"Unknown Error Code 0x{code:X8}";
            }
        }

        private static string WarningCodeToMessage(uint code)
        {
            switch (code)
            {
                case 0x00020001: return "Ribbon low";
                case 0x00020002: return "Card low 3";
                case 0x00020005: return "Card low 2";
                case 0x00020006: return "Card low 1";
                case 0x00020003: return "Waiting for card insertion";
                case 0x00020004: return "Waiting for card removal";
                case 0x00020009: return "Cleaning procedure recommended";
                case 0x0002000A: return "Data unidentified";
                case 0x00020011: return "Magstripe is blank";
                case 0x00020014: return "Magstripe data error";
                default: return $"Warning Code 0x{code:X8}";

            }
        }
    }


}
