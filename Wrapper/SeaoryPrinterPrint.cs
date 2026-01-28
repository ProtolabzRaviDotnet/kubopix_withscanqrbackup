using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.Wrapper
{
    public static class SeaoryPrinterPrint
    {
        private const string DLL_NAME = "SeaorySDK.dll";

        private static void Log(string msg)
        {
            AppLogger.Log("[SEAORY] " + msg);
        }

        // ---------------------------------------------------------
        // DLL IMPORTS
        // ---------------------------------------------------------
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_GetPrinterStatusA(string szPrinterName, out uint lpdwStatus);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_GetPrinterWarningA(string szPrinterName, out uint lpdwWarning);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_StartPrinting2A(string szPrinterName, IntPtr pDocProp, out IntPtr hDC);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SOY_PR_StartPage2(IntPtr hPrinterDC);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint SOY_PR_PrintImage2A(IntPtr hPrinterDC, int x, int y, int width, int height, string imagePath);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SOY_PR_EndPage2(IntPtr hPrinterDC);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SOY_PR_EndPrinting2(IntPtr hPrinterDC, bool bCancelJob);

        // ---------------------------------------------------------
        // CORRECT 259-BYTE SEAORY STRUCT
        // ---------------------------------------------------------
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SEAORY_DOC_PROP
        {
            public byte byOrientation;       // 1 portrait, 2 landscape
            public byte byRibbonType;
            public byte byPrintSide;
            public byte byPrintPanelFront;
            public byte byPrintPanelBack;
            public byte byResinKfront;
            public byte byResinKback;
            public byte byRotate180;

            public byte byInputBin;
            public byte byFeedCardMode;
            public byte byOutputBin;
            public byte byEjectCardMode;
            public byte byCardInOutByDev;
            public byte byResolution;        // ⭐ 1 = 300×600 DPI
            public byte byEraseCard;

            public byte byAutoDetectRibbon;
            public byte byMirror;
            public byte byMonoSpeedMode;

            public byte byGrayYMC;
            public byte byPaperSize;         // 0 = CR80

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] byReserved0;

            public byte by300x1200Mode;
            public byte bySecurityErase;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 229)]
            public byte[] byReserved;
        }



        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern uint SOY_PR_SetPrinterSettingA([MarshalAs(UnmanagedType.LPStr)] string szPrinterNameA, IntPtr lpDocPropIn);


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern uint SOY_PR_GetPrintAreaA(string szPrinterNameA, int nOrientation, int nSide, int nPanel, out int lpnAreaNum, out int lpcbBytes, StringBuilder lpAreaNameAll, StringBuilder lpAreaNameDefault);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern uint SOY_PR_SetPrintAreaA(string szPrinterNameA, int nOrientation, int nSide, int nPanel, string szAreaNameDefault);



        // ---------------------------------------------------------
        // STATUS HELPERS
        // ---------------------------------------------------------
        public static string CheckPrinterStatus(string printerName)
        {
            Log($"Checking Status for {printerName}");

            uint status;
            uint r = SOY_PR_GetPrinterStatusA(printerName, out status);

            Log($"Status Result={r}, Value=0x{status:X8}");

            if (r != 0) return $"SDK Error {r}";
            return status == 0 ? "Ready" : $"Printer Error 0x{status:X8}";
        }

        public static string CheckPrinterWarning(string printerName)
        {
            Log($"Checking Warning for {printerName}");

            uint warning;
            uint r = SOY_PR_GetPrinterWarningA(printerName, out warning);

            Log($"Warning Result={r}, Value=0x{warning:X8}");

            if (r != 0) return $"SDK Error {r}";
            return warning == 0 ? "No Warnings" : $"Warning 0x{warning:X8}";
        }

        // ---------------------------------------------------------
        // SET PRINTER SETTINGS
        // ---------------------------------------------------------
        public static string SetPrinterSetting(string printerName, int orientation)
        {
            Log("Preparing Printer Settings (300×600 DPI + CR80)...");

            var prop = new SEAORY_DOC_PROP
            {
                byOrientation = (byte)orientation,                // Landscape
                byRibbonType = 0,
                byPrintSide = 1,
                byPrintPanelFront = 0,
                byPrintPanelBack = 0,
                byResinKfront = 0,
                byResinKback = 0,
                byRotate180 = 0,

                byInputBin = 0,
                byFeedCardMode = 0,
                byOutputBin = 0,
                byEjectCardMode = 0,
                byCardInOutByDev = 1,

                byResolution = 1,                 // ⭐ Enable 300×600 DPI
                byEraseCard = 0,

                byAutoDetectRibbon = 1,
                byMirror = 0,
                byMonoSpeedMode = 0,

                byGrayYMC = 0,
                byPaperSize = 0,                  // CR80

                byReserved0 = new byte[5],
                by300x1200Mode = 0,
                bySecurityErase = 0,
                byReserved = new byte[229]
            };

            int size = Marshal.SizeOf(typeof(SEAORY_DOC_PROP));
            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.StructureToPtr(prop, ptr, false);

            uint result = SOY_PR_SetPrinterSettingA(printerName, ptr);
            Marshal.FreeHGlobal(ptr);

            Log($"SetPrinterSetting returned {result}");

            return result == 0 ? "OK" : $"SetPrinterSetting Error {result}";
        }

        // ---------------------------------------------------------
        // GET PRINT AREA
        // ---------------------------------------------------------
        public static string GetFirstPrintAreaName(string printerName, int orientation)
        {
            Log($"GetPrintArea for Orientation={orientation}");

            int areaNum = 0;
            int bytes = 0;

            var allNames = new StringBuilder(4096);
            var defaultName = new StringBuilder(512);

            uint r = SOY_PR_GetPrintAreaA(printerName, orientation, 0, 0, out areaNum, out bytes, allNames, defaultName);

            Log($"GetPrintArea Result={r}, AreaNum={areaNum}, Bytes={bytes}");
            Log($"All Areas Raw={allNames}");
            Log($"Default Area={defaultName}");

            //if (r != 0) return null;
            if (r != 0) return string.Empty;

            string[] parts = allNames.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length > 0 ? parts[0] : defaultName.ToString();
        }

        // ---------------------------------------------------------
        // PRINT PROCESS
        // ---------------------------------------------------------
        public static string PrintImage(string printerName, string imagePath)
        {
            Log($"Starting PrintImage: {imagePath}");
            // -----------------------------------
            // 1) SET PRINTER SETTINGS (LANDSCAPE)
            // -----------------------------------
            string prep = SetPrinterSetting(printerName, 2); // 2 = Landscape
            Log($"SetPrinterSetting Result: {prep}");
            if (prep != "OK")
                return prep;
            // -----------------------------------
            // 2) FIX CRITICAL ISSUE:
            // SET PRINT AREA TO LANDSCAPE
            // -----------------------------------
            Log("Fetching Landscape PrintArea…");
            string areaName = GetFirstPrintAreaName(printerName, 2); // 2 = landscape
            if (string.IsNullOrEmpty(areaName))
            {
                Log("ERROR: No print area found for landscape!");
                return "PrintArea Missing";
            }
            Log("Using PrintArea: " + areaName);
            uint areaSet = SOY_PR_SetPrintAreaA(printerName, 2, 0, 0, areaName);
            Log($"SetPrintArea returned {areaSet}");
            if (areaSet != 0)
                return $"SetPrintArea Error {areaSet}";
            // -----------------------------------
            // 3) START PRINT JOB
            // -----------------------------------
            IntPtr hDC;
            Log("Calling StartPrinting2A...");
            uint r1 = SOY_PR_StartPrinting2A(printerName, IntPtr.Zero, out hDC);
            Log($"StartPrinting2A returned {r1}, hDC={hDC}");
            if (r1 != 0)
                return $"StartPrinting Error {r1}";
            // -----------------------------------
            // 4) START PAGE
            // -----------------------------------
            uint r2 = SOY_PR_StartPage2(hDC);
            Log($"StartPage2 returned {r2}");
            if (r2 != 0)
                return $"StartPage Error {r2}";
            // -----------------------------------
            // 5) PRINT IMAGE (LANDSCAPE CR-80)
            // -----------------------------------
            // FIXED: Correct dimensions for CR80 landscape @ 300x600 DPI
            // (horizontal: 600 DPI long side ~3.37" = 2022 px; vertical: 300 DPI short side ~2.125" = 637 px)
            int W = 1011;
            int H = 637;
               Log($"Printing Image W={W}, H={H}");
            uint r3 = SOY_PR_PrintImage2A(hDC, 0, 0, W, H, imagePath);
            Log($"PrintImage2A returned {r3} (hex: 0x{r3:X})");
            if (r3 != 0)
            {
                Log("ERROR: Image OUTSIDE printable area (0x11013)");
                return $"PrintImage Error {r3}";
            }
            // -----------------------------------
            // 6) END PAGE + END PRINT
            // -----------------------------------
            uint r4 = SOY_PR_EndPage2(hDC);
            Log($"EndPage2 returned {r4}");
            uint r5 = SOY_PR_EndPrinting2(hDC, false);
            Log($"EndPrinting2 returned {r5}");
            return "OK";
        }
    }
}


