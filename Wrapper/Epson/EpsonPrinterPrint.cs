using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using kuboPix;
using com.epson.label.driver;
using kuboPix.Helpers;

namespace kuboPix.Wrapper
{
    public static class EpsonPrinterPrint
    {
        private static void Log(string msg)
        {
            AppLogger.Log("[EPSON] " + msg);
        }

        /// <summary>
        /// Prints the specified image file to the Epson label printer.
        /// </summary>
        /// <param name="devName">Epson Printer Device Name</param>
        /// <param name="portName">Epson Printer Port Name (can be empty/spooler direct)</param>
        /// <param name="imagePath">Path to image file on local disk</param>
        /// <returns>"OK" if successful, otherwise error message</returns>
        public static string PrintImage(string devName, string portName, string imagePath)
        {
            try
            {
                Log($"Starting Epson PrintImage for: '{imagePath}' on printer '{devName}'");

                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    return $"Error: Image file does not exist at '{imagePath}'";
                }

                // Read image file bytes
                byte[] imageBytes = File.ReadAllBytes(imagePath);

                // Print the image using raw ESC/Label commands via standard Windows Spooler
                bool success = EpsonPrinterHelper.PrintPngImage(devName, imageBytes);

                if (success)
                {
                    Log("Epson Print job successfully Print");
                    return "OK";
                }
                else
                {
                    return "Error: Failed to send raw print job to Windows Spooler.";
                }
            }
            catch (Exception ex)
            {
                Log($"Epson print execution failed: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
