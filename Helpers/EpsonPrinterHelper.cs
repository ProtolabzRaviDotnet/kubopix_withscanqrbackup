using System;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.Helpers
{
    public static class EpsonPrinterHelper
    {
        // P/Invoke structures and methods
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);



        /// <summary>
        /// Spools raw byte array payload directly to the target printer queue using Win32 API.
        /// </summary>
        public static bool SendRawBytesToUsbSpooler(string printerName, byte[] payload)
        {
            if (string.IsNullOrEmpty(printerName) || payload == null || payload.Length == 0)
                return false;

            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOA di = new DOCINFOA { pDocName = "Epson Direct Graphics Job", pDataType = "RAW" };
            bool bSuccess = false;

            try
            {
                if (OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    try
                    {
                        if (StartDocPrinter(hPrinter, 1, di))
                        {
                            try
                            {
                                if (StartPagePrinter(hPrinter))
                                {
                                    try
                                    {
                                        int dwWritten = 0;
                                        IntPtr pBytes = Marshal.AllocCoTaskMem(payload.Length);
                                        try
                                        {
                                            Marshal.Copy(payload, 0, pBytes, payload.Length);
                                            bSuccess = WritePrinter(hPrinter, pBytes, payload.Length, out dwWritten);
                                            if (!bSuccess)
                                            {
                                                int err = Marshal.GetLastWin32Error();
                                                AppLogger.Log($"[EPSON] WritePrinter failed. Win32 Error: {err}");
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.FreeCoTaskMem(pBytes);
                                        }
                                    }
                                    finally
                                    {
                                        EndPagePrinter(hPrinter);
                                    }
                                }
                                else
                                {
                                    int err = Marshal.GetLastWin32Error();
                                    AppLogger.Log($"[EPSON] StartPagePrinter failed. Win32 Error: {err}");
                                }
                            }
                            finally
                            {
                                EndDocPrinter(hPrinter);
                            }
                        }
                        else
                        {
                            int err = Marshal.GetLastWin32Error();
                            AppLogger.Log($"[EPSON] StartDocPrinter failed. Win32 Error: {err}");
                        }
                    }
                    finally
                    {
                        ClosePrinter(hPrinter);
                    }
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    AppLogger.Log($"[EPSON] OpenPrinter failed for '{printerName}'. Win32 Error: {err}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[EPSON] Critical error in SendRawBytesToUsbSpooler: {ex.Message}");
            }

            return bSuccess;
        }

        /// <summary>
        /// Combines binary PNG graphics payload with ESC/Label commands, prints it, and purges from DRAM (Synchronous).
        /// </summary>
        public static bool PrintPngImage(string printerName, byte[] pngBytes)
        {
            if (string.IsNullOrEmpty(printerName) || pngBytes == null || pngBytes.Length == 0)
                return false;

            // 1. Delete previous image to keep DRAM clean before downloading (Using StringBuilder)
            var clearBuilder = new StringBuilder();
            clearBuilder.AppendLine("^XA");
            clearBuilder.AppendLine("^IDR:UPLOAD.PNG^FS");
            clearBuilder.AppendLine("^XZ");
            byte[] clearBytes = Encoding.ASCII.GetBytes(clearBuilder.ToString());

            // 2. Download the PNG image to DRAM (R: drive)
            byte[] headerBytes = Encoding.ASCII.GetBytes($"~DYR:UPLOAD.PNG,B,P,{pngBytes.Length},0,");

            // 3. Print the image using ^IL (Image Load) and trigger Autocut (Using StringBuilder)
            var printBuilder = new StringBuilder();
            printBuilder.AppendLine("^XA");
            printBuilder.AppendLine("^S(CLR,R,600");
            printBuilder.AppendLine("^S(CLR,P,600");
            printBuilder.AppendLine("^S(CLS,P,2017");
            printBuilder.AppendLine("^S(CLS,L,1276");

            // --- INTEGRATED AUTOCUT COMMANDS ---
            printBuilder.AppendLine("^MMC,Y"); // Cutter Mode: Cut after printing, with backfeed

            printBuilder.AppendLine("^FO0,0^ILR:UPLOAD.PNG^FS");
            printBuilder.AppendLine("^XZ");

            // --- IMMEDIATE CUT COMMAND ---
            printBuilder.AppendLine("~JK"); // Immediate Cut/Cut Now trigger command

            byte[] printBytes = Encoding.ASCII.GetBytes(printBuilder.ToString());

            // Combine all byte segments into a single raw transaction payload
            byte[] payload = new byte[clearBytes.Length + headerBytes.Length + pngBytes.Length + printBytes.Length];
            Buffer.BlockCopy(clearBytes, 0, payload, 0, clearBytes.Length);
            Buffer.BlockCopy(headerBytes, 0, payload, clearBytes.Length, headerBytes.Length);
            Buffer.BlockCopy(pngBytes, 0, payload, clearBytes.Length + headerBytes.Length, pngBytes.Length);
            Buffer.BlockCopy(printBytes, 0, payload, clearBytes.Length + headerBytes.Length + pngBytes.Length, printBytes.Length);

            return SendRawBytesToUsbSpooler(printerName, payload);
        }

        /// <summary>
        /// Combines binary PNG graphics payload with ESC/Label commands, prints it, and purges from DRAM (Asynchronous).
        /// </summary>
        public static async Task<bool> PrintPngImageAsync(string printerName, byte[] pngBytes)
        {
            if (string.IsNullOrEmpty(printerName) || pngBytes == null || pngBytes.Length == 0)
                return false;

            return await Task.Run(() => PrintPngImage(printerName, pngBytes));
        }


    }
}
