using kuboPix;
using kuboPix.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management;
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
                                                string errMsg = new Win32Exception(err).Message;
                                                AppLogger.Log($"[EPSON] WritePrinter failed. Win32 Error: {err} ({errMsg})");
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
                                    string errMsg = new Win32Exception(err).Message;
                                    AppLogger.Log($"[EPSON] StartPagePrinter failed. Win32 Error: {err} ({errMsg})");
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
                            string errMsg = new Win32Exception(err).Message;
                            AppLogger.Log($"[EPSON] StartDocPrinter failed. Win32 Error: {err} ({errMsg})");
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
                    string errMsg = new Win32Exception(err).Message;
                    AppLogger.Log($"[EPSON] OpenPrinter failed for '{printerName}'. Win32 Error: {err} ({errMsg})");
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

            // 1. Download the PNG image directly to DRAM (R: drive) - ~DY automatically overwrites existing image
            byte[] headerBytes = Encoding.ASCII.GetBytes($"~DYR:UPLOAD.PNG,B,P,{pngBytes.Length},0,");

            // 2. Print the image using ^IL (Image Load) and trigger Autocut
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

            // Combine byte segments into a single raw transaction payload
            byte[] payload = new byte[headerBytes.Length + pngBytes.Length + printBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
            Buffer.BlockCopy(pngBytes, 0, payload, headerBytes.Length, pngBytes.Length);
            Buffer.BlockCopy(printBytes, 0, payload, headerBytes.Length + pngBytes.Length, printBytes.Length);

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
        [DllImport("winspool.Drv", EntryPoint = "SetJobA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool SetJob(IntPtr hPrinter, int JobId, int Level, IntPtr pJob, int Command);

        /// <summary>
        /// Purges (cancels) all pending print jobs for the specified printer and all installed Epson printers
        /// and sends ESC/Label cancel commands to clear hardware printer DRAM buffer.
        /// </summary>
        public static void PurgePrinterQueue(string? printerName)
        {
            var targetPrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var installedList = new List<string>();
                foreach (string installed in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    installedList.Add(installed);
                }

                string? sessionName = LoginView.GetCurrentSessionPrinterName();
                if (!string.IsNullOrWhiteSpace(sessionName))
                {
                    var match = installedList.FirstOrDefault(p => p.Equals(sessionName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match != null) targetPrinters.Add(match);
                }

                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    var match = installedList.FirstOrDefault(p => p.Equals(printerName.Trim(), StringComparison.OrdinalIgnoreCase))
                             ?? installedList.FirstOrDefault(p => p.IndexOf(printerName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || printerName.Trim().IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) targetPrinters.Add(match);
                }

                // Match any installed Epson / CW- label printers
                foreach (string installed in installedList)
                {
                    if (installed.IndexOf("Epson", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        installed.IndexOf("CW-", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetPrinters.Add(installed);
                    }
                }
            }
            catch { }

            foreach (string pName in targetPrinters)
            {
                ClearSinglePrinterQueue(pName);
            }
        }

        private static void ClearSinglePrinterQueue(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return;

            AppLogger.Log($"[EPSON] Clearing print jobs for '{printerName}'...");

            // 1. WMI Delete Print Jobs (cancels and deletes pending/stuck spooler jobs first)
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PrintJob WHERE Name LIKE '%{printerName.Replace("'", "''")}%'"))
                {
                    foreach (ManagementObject job in searcher.Get())
                    {
                        try
                        {
                            job.Delete();
                            AppLogger.Log($"[EPSON] Successfully deleted active print job for '{printerName}'.");
                        }
                        catch (Exception exJob)
                        {
                            AppLogger.Log($"[EPSON] Error deleting print job via WMI: {exJob.Message}");
                        }
                    }
                }
            }
            catch (Exception exWmi)
            {
                AppLogger.Log($"[EPSON] WMI print job search exception for '{printerName}': {exWmi.Message}");
            }

            // 2. Send immediate ESC/Label runtime cancel (~JA) and unpause (~PS).
            // Do NOT send ^XA...^XZ because empty format blocks cause the printer to feed out blank labels.
            try
            {
                var cancelBuilder = new StringBuilder();
                cancelBuilder.AppendLine("~JA"); // Cancel All active/buffered format jobs immediately in firmware
                cancelBuilder.AppendLine("~PS"); // Ensure printer is unpaused and ready

                byte[] cancelBytes = Encoding.ASCII.GetBytes(cancelBuilder.ToString());
                bool sent = SendRawBytesToUsbSpooler(printerName, cancelBytes);
                if (sent)
                {
                    AppLogger.Log($"[EPSON] Sent pure ESC/Label runtime Cancel All & Resume (~JA, ~PS) to '{printerName}'.");
                }
            }
            catch (Exception exCmd)
            {
                AppLogger.Log($"[EPSON] Failed to send cancel commands for '{printerName}': {exCmd.Message}");
            }
        }
    }
}
