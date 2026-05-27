using Microsoft.Identity.Client;
using MiniApp.Helpers;
using MiniApp.ViewModels;
using MiniApp.Views;
using MiniApp.Wrapper;
using Newtonsoft.Json;
using QRCoder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MiniApp.Services
{
    public class OrderQueueService
    {
        // Fields
        private readonly string _jwtToken;
        private readonly Int64 _shopLoginId;
        private readonly Int64 _printerId;
        private readonly string _printerName;
        private readonly HttpClient _client;

        private bool _isProcessing = false;

        private bool _isStarted = false;

        private CancellationTokenSource _cts = new CancellationTokenSource();

        private int _printerStatus = 0;
        private string _printerMessage = "";

        private string _qrCodeData;
        private decimal _printerPrice;
        private DateTime _qrExpiry;

        private int _printerPaymentEnabled;

        private static HashSet<long> _processedOrderIds = new HashSet<long>();
        public OrderQueueService(string jwtToken, long shopLoginId, long printerId,string printerName, int printerStatus,string qrCodeData, decimal printerPrice, DateTime qrExpiry,int printerPaymentEnabled)
        {
            _jwtToken = jwtToken;
            _shopLoginId = shopLoginId;
            _printerId = printerId;
            _printerName = printerName;
            _printerStatus = printerStatus;

            _qrCodeData = qrCodeData;
            _printerPrice = printerPrice;
            _qrExpiry = qrExpiry;
            _printerPaymentEnabled = printerPaymentEnabled;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _jwtToken);

            //AppLogger.Log("OrderQueueService initialized.");
        }

        //Start the queue process in background (idempotent)
        public void StartQueue()
        {
            if (_isStarted) return;
            _isStarted = true;

            AppLogger.Log("Queue started.");

            // Main queue loop
            _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (!_isProcessing)
                            await ProcessPendingOrders();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("StartQueue Loop Error: " + ex.Message);
                    }

                    // check every 15 seconds
                    await Task.Delay(15000, _cts.Token).ContinueWith(t => { });
                }
            }, _cts.Token);

            // start printer monitoring
            StartPrinterMonitoring();
        }

        // Stop the queue and monitoring (useful for cleanup)
        public void StopQueue()
        {
            try
            {
                if (!_cts.IsCancellationRequested)
                    _cts.Cancel();
            }
            catch (Exception ex)
            {
                AppLogger.Log("StopQueue error: " + ex.Message);
            }
        }

        // Monitor printer status in background (debounced)
        private void StartPrinterMonitoring()
        {
            //AppLogger.Log("Printer monitoring started.");

            _ = Task.Run(async () =>
            {
                int consecReady = 0;
                int consecNotReady = 0;
                const int requiredConsecutive = 3; // require 3 consistent reads
                int previousStatus = _printerStatus; // <-- 1. Moved it outside the loop (saves the previous status).

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {

                        string status = SeaoryPrinterPrint.CheckPrinterStatus(_printerName);
                        string warning = SeaoryPrinterPrint.CheckPrinterWarning(_printerName);

                        //AppLogger.Log($"Printer Raw Status: {status}");
                        //AppLogger.Log($"Printer Warning: {warning}");

                        // We parse messages but avoid flipping state on single reads
                        string parsedMessage = GetMessageFromErrorCodes(status, warning) ?? "";

                        bool isReadyNow = status == "Ready";

                        if (isReadyNow)
                        {
                            consecReady++;
                            consecNotReady = 0;
                        }
                        else
                        {
                            consecNotReady++;
                            consecReady = 0;
                        }

                        // Only flip to READY if we have required consecutive ready reads
                        if (consecReady >= requiredConsecutive && _printerStatus != 1)
                        {
                            _printerStatus = 1;
                            _printerMessage = parsedMessage;
                        }
                        // Only flip to a non-ready state after required consecutive non-ready reads
                        else if (consecNotReady >= requiredConsecutive)
                        {
                            // Call GetMessageFromErrorCodes to ensure _printerStatus and _printerMessage are set consistently
                            GetMessageFromErrorCodes(status, warning);
                        }
                        else
                        {
                            // Do not change _printerStatus yet
                            _printerMessage = parsedMessage;
                        }

                        //AppLogger.Log($"Printer Status Parsed: {_printerStatus} | {_printerMessage}");

                        if (previousStatus != _printerStatus)
                        {
                            previousStatus = _printerStatus; // <-- 2. Updated the previous status whenever the state changes.
                            await UpdatePrinterStatus(_printerId, _shopLoginId, _printerStatus, _printerMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("Printer Monitor Error: " + ex.Message);
                    }

                    // every 1 minute
                    await Task.Delay(60000, _cts.Token).ContinueWith(t => { });
                }
            }, _cts.Token);
        }

        // Process pending orders
        private async Task ProcessPendingOrders()
        {
            try
            {
                _isProcessing = true;
                //AppLogger.Log("Checking for pending orders...");

                var pendingOrders = await GetPendingOrders();
                //AppLogger.Log($"Pending Orders Retrieved: {pendingOrders.Count}");

                // Filter: Remove orders already processed earlier
                var newOrders = pendingOrders
                    .Where(o => !_processedOrderIds.Contains(o.OrderId))
                    .ToList();

                if (newOrders.Count == 0)
                {
                    //AppLogger.Log("No new pending orders found.");
                    return;
                }

                //AppLogger.Log($"Processing {newOrders.Count} new orders...");

                //Working Code to process each order
                foreach (var order in newOrders)
                {
                    //AppLogger.Log($"Processing Order #{order.OrderId}");

                    // 1. PRINT START → Navigate to PrintProgressView on UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var printProgressView = new PrintProgressView();
                        NavigationService.Navigate(printProgressView);  // Set the current page as PrintProgressView
                    });

                    if (_processedOrderIds.Contains(order.OrderId))
                    {
                        AppLogger.Log($"This Order is Already Processed {order.OrderId}");

                        continue;
                    }
       
                        // MARK as processed
                        _processedOrderIds.Add(order.OrderId);

                        var processedIdsString = string.Join(", ", _processedOrderIds);
                        //AppLogger.Log($"Processed Order Ids First: {processedIdsString}");

                        var (isSuccess, message) = await PrintOrderAsync(order);

                       //AppLogger.Log($"Success: {isSuccess}, Message: {message}");

                    if (isSuccess)
                        {
                            //AppLogger.Log($"Print success for Order {order.OrderId}");
                            
                            // Wait for timer completion in PrintProgressView
                            while (!(NavigationService.GetCurrentPage() is PrintProgressView printProgressView && printProgressView.IsTimerCompleted()))
                            {
                                await Task.Delay(500);  // Wait until the timer completes
                            }

                            if (order.IsPaidOrder == 1)
                            {
                                   // Capture Payment ONLY for Stripe payments
                                   if (order.OrderPaymentTypeId == 1) // 1 = Stripe
                                   {
                                       await CapturePaymentAsync(order);
                                   }

                                await UpdateOrderStatus(order.OrderId, 1, order.PaymentIntentId ?? string.Empty, orderPaymentTypeId: order.OrderPaymentTypeId); // success

                                //AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — Navigating to PrintSuccessView");

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Helpers.NavigationService.Navigate(new PrintSuccessView());
                                });

                                //AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — PrintSuccessView shown, starting 5s delay");

                            // Wait 5 seconds, then return to ScanQrView
                            await Task.Delay(5000);

                            //AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — 5s delay done, navigating to ScanQrView");

                            // ← TRY-CATCH 
                            try
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Helpers.NavigationService.Navigate(new ScanQrView(
                                        _jwtToken,
                                        _shopLoginId,
                                        _printerId,
                                        _printerName,
                                        _qrCodeData,
                                        _printerPrice,
                                        _printerStatus,
                                        _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                                        _printerPaymentEnabled

                                    ));
                                });

                                //AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — ScanQrView navigation done successfully");

                            }
                            catch (Exception navEx)
                            {
                                AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — Navigation failed: {navEx.Message}");
                                // FALLBACK: Load Data From Session and Navigate
                                try
                                {
                                    NavigateBackToScanQR();  
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — Fallback also failed: {ex.Message}");
                                }
                            }

                            }
                            else
                            {

                                // Free order
                                await UpdateOrderStatusFree(order.OrderId, 1, orderPaymentTypeId: order.OrderPaymentTypeId); // success

                            //AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — Navigating to PrintSuccessView");

                            Application.Current.Dispatcher.Invoke(() =>
                                {
                                    Helpers.NavigationService.Navigate(new PrintSuccessView());
                                });

                            //AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — PrintSuccessView shown, starting 5s delay");

                            await Task.Delay(5000);

                            //AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — 5s delay done, navigating to ScanQrView");

                            // TRY-CATCH for free order navigation back
                            try
                            {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        Helpers.NavigationService.Navigate(new ScanQrView(
                                            _jwtToken,
                                            _shopLoginId,
                                            _printerId,
                                            _printerName,
                                            _qrCodeData,
                                            _printerPrice,
                                            _printerStatus,
                                            _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                                            _printerPaymentEnabled
                                        ));
                                    });

                                //AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — ScanQrView navigation done successfully");

                            }
                                catch (Exception navEx)
                                {
                                AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — Navigation failed: {navEx.Message}");
                                try
                                    {
                                        NavigateBackToScanQR();
                                    }
                                    catch (Exception ex)
                                    {
                                    AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — Fallback also failed: {ex.Message}");
                                }
                                }
                            }
                        }
                        else
                        {
                        // SAVE FAILED ORDER IN SESSION
                        FailedPrintSession.Add(new PendingOrder
                        {
                            OrderId = order.OrderId,
                            Image = order.Image,
                            PaymentIntentId = order.PaymentIntentId,
                            ShopLoginId = order.ShopLoginId,
                            PrinterId = order.PrinterId,
                            IsPaidOrder = order.IsPaidOrder,
                            OrderPaymentTypeId = order.OrderPaymentTypeId,
                            OrderStatus = order.OrderStatus
                        });

                        var failedOrder = FailedPrintSession.GetAll().LastOrDefault();

                        //AppLogger.Log($"Print failed for Order {order.OrderId}");

                        if (order.IsPaidOrder == 1 && (order.OrderPaymentTypeId == 1 || order.OrderPaymentTypeId == 3))//OrderPaymentTypeId = 1 For Stripe or OrderPaymentTypeId = 3 for Twint
                        {
                            await UpdateOrderStatus(order.OrderId, 2, order.PaymentIntentId ?? string.Empty, message, order.OrderPaymentTypeId);

                    }
                            else
                            {
                                await UpdateOrderStatusFree(order.OrderId, 2, message, order.OrderPaymentTypeId); // failed
                            }

                            //Send Email on Print Fail and Update Status
                             await UpdatePrinterStatus(_printerId, _shopLoginId, _printerStatus, message);

                            // Immediately stop timer and navigate to error view on UI thread
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                // Stop the timer in PrintProgressView if it's the current page
                                var currentPage = NavigationService.GetCurrentPage();
                                
                                //AppLogger.Log($"Current Page before navigating to error view (ProcessPendingOrders): {currentPage?.GetType().Name}");

                                if (currentPage is PrintProgressView printProgressView)
                                {
                                    printProgressView.StopTimer();

                                    //AppLogger.Log("Stopped PrintProgressView timer due to print failure.");
                                }

                                AppLogger.Log($"Navigating to PrintErrorView with message (ProcessPendingOrders): {message}");
                                // Navigate to PrintErrorView with the real error message
                                NavigationService.Navigate(new PrintErrorView(message, failedOrder, order.OrderPaymentTypeId));

                            });
                               
                    }

                        // MARK as processed
                        var processedIdsString2 = string.Join(", ", _processedOrderIds);
                        //AppLogger.Log($"Processed Order Ids Last 2: {processedIdsString2}");
                   
                    await Task.Delay(2000);
                }



            }
            catch (Exception ex)
            {
                AppLogger.Log("Queue Processing Error: " + ex.Message);
            }
            finally
            {
                _isProcessing = false;
            }
        }
                
        // Fetch pending orders from API
        private async Task<List<PendingOrder>> GetPendingOrders()
        {
            try
            {
                var url = $"{AppConfig.ApiBaseUrl}api/get/pendingorders?shopLoginId={_shopLoginId}&printerId={_printerId}";
                //AppLogger.Log($"Requesting Pending Orders: {url}");

                var resp = await _client.GetAsync(url);

                if (!resp.IsSuccessStatusCode)
                    return new List<PendingOrder>();

                var json = await resp.Content.ReadAsStringAsync();
                dynamic? result = JsonConvert.DeserializeObject(json);

                //AppLogger.Log($"Pending Orders API Response: {json}");

                if (result == null)
                    return new List<PendingOrder>();

                if (result.status == 1 && result.data != null)
                {
                    string dataJson = JsonConvert.SerializeObject(result.data);
                    return JsonConvert.DeserializeObject<List<PendingOrder>>(dataJson)
                           ?? new List<PendingOrder>();
                }

                return new List<PendingOrder>();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Exception : GetPendingOrders: {ex.Message}");
                // In case of a network timeout or failure, return an empty list to keep the app running safely
                return new List<PendingOrder>();
            }
        }

        // Load Image from URL
        private async Task<Image?> LoadImageFromUrlAsync(string url)
        {
            try
            {
                //AppLogger.Log($"[IMAGE] Download start: {url}");

                var response = await _client.GetAsync(url);

                // IMAGE DELETED / EXPIRED
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    AppLogger.Log($"[IMAGE] NOT FOUND (deleted/expired): {url}");
                    return null;
                }

                // ANY OTHER FAILURE
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"[IMAGE] Download failed. Status={response.StatusCode}, Url={url}");
                    return null;
                }

                using (var ms = new MemoryStream())
                {
                    await response.Content.CopyToAsync(ms);

                    // EMPTY IMAGE CHECK
                    if (ms.Length == 0)
                    {
                        AppLogger.Log($"[IMAGE] EMPTY IMAGE STREAM: {url}");
                        return null;
                    }

                    ms.Position = 0;

                    var img = Image.FromStream(ms);

                    //AppLogger.Log($"[IMAGE] Download success: {url}");

                    return img;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[IMAGE] Exception while loading image: {ex}");
                return null;
            }
        }

        // Resize Image to CR80 (1011 x 637px @ 300 DPI) - with orientation lock
        private string ResizeAndCropToCard(Image img)
        {
            try
            {

                //AppLogger.Log("Resizing image to CR80 card size...");

                const int CardWidthPx = 1011;
                const int CardHeightPx = 637;
                const int Margin = 2;

                // Ensure landscape orientation before processing
                if (img.Height > img.Width)
                {
                    img.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    AppLogger.Log("Image rotated to landscape.");
                }

                double cardRatio = (double)CardWidthPx / CardHeightPx;
                double imgRatio = (double)img.Width / img.Height;

                int newWidth, newHeight;

                if (imgRatio > cardRatio)
                {
                    newHeight = CardHeightPx;
                    newWidth = (int)Math.Round(imgRatio * CardHeightPx);
                }
                else
                {
                    newWidth = CardWidthPx;
                    newHeight = (int)Math.Round(CardWidthPx / imgRatio);
                }

                using (var bmp = new Bitmap(newWidth, newHeight))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.DrawImage(img, 0, 0, newWidth, newHeight);

                    Rectangle cropRect = new Rectangle(
                        Math.Max(0, (newWidth - CardWidthPx) / 2),
                        Math.Max(0, (newHeight - CardHeightPx) / 2),
                        CardWidthPx - Margin,
                        CardHeightPx - Margin
                    );

                    using (var finalBmp = bmp.Clone(cropRect, bmp.PixelFormat))
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(),
                            "Seaory_PrintReady_" + Guid.NewGuid() + ".jpg");

                        finalBmp.SetResolution(300, 300);
                        finalBmp.Save(tempFile, System.Drawing.Imaging.ImageFormat.Jpeg);

                        ///AppLogger.Log($"Image resized and saved: {tempFile}");

                        return tempFile;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error in ResizeAndCropToCard: {ex.Message}");
                return string.Empty;
            }
        }

        //Print Order
        public async Task<(bool, string)> PrintOrderAsync(PendingOrder order)
        {
            string? resizedPath = null;

            try
            {
                //AppLogger.Log($"Starting print for Order {order.OrderId}");
                
                if (string.IsNullOrEmpty(order.Image))
                {
                    AppLogger.Log("Image URL missing.");
                    return (false, "Image URL missing");
                }

                // STEP 1 — Load image
                var img = await LoadImageFromUrlAsync(order.Image);

                if (img == null)
                {
                    AppLogger.Log("[PRINT] Failed to load image (deleted / expired)");
                    return (false, "Print failed: the image has expired and was automatically removed after 30 minutes. Please re-scan the QR code and try again.");
                }

                // STEP 2 — Ensure disposal AFTER null check
                using (img)
                {
                    AppLogger.Log("Resizing image to CR80 300 DPI...");
                    resizedPath = ResizeAndCropToCard(img);
                    AppLogger.Log($"Resized file created: {resizedPath}");
                } // image disposed here safely

                string warning = SeaoryPrinterPrint.CheckPrinterWarning(_printerName);

                if (warning != "No Warnings")
                    AppLogger.Log($"Printer Warning: {warning}");

                // STEP 3 — Check printer status
                string status = SeaoryPrinterPrint.CheckPrinterStatus(_printerName);
                if (status != "Ready")
                {
                    string parsedMessage = GetMessageFromErrorCodes(status, warning) ?? "";

                    AppLogger.Log($"Printer not ready: {status}");
                    AppLogger.Log($"Parsed Error Message: {parsedMessage}");
                    return (false, parsedMessage);
                }

                // STEP 4 — Print
                AppLogger.Log($"Printing resized file: {resizedPath}");
                string printResult = SeaoryPrinterPrint.PrintImage(_printerName, resizedPath);

                AppLogger.Log($"Print Result: {printResult}");
                AppLogger.Log($"Print Result OrderId: {order.OrderId}");

                if (printResult != "OK")
                {
                    AppLogger.Log($"Driver Print Failed: {printResult}");
                    return (false, printResult);
                }

                // STEP 4 — WAIT FOR REAL HARDWARE RESULT
                var (finalSuccess, finalError) =
                    await WaitForPrintCompletionAsync(_printerName);

                if (!finalSuccess)
                {
                    AppLogger.Log($"Hardware Print Failed: {finalError}");
                    return (false, finalError);
                }

                if (finalSuccess)
                {
                    // Delete temp file after short delay with retries (avoid race with driver)
                    _ = Task.Run(async () =>
                    {
                        if (string.IsNullOrEmpty(resizedPath)) return;
                        int attempts = 0;
                        while (attempts < 6)
                        {
                            try
                            {
                                if (File.Exists(resizedPath))
                                {
                                    File.Delete(resizedPath);
                                    AppLogger.Log("TEMP file deleted after successful print.");
                                }
                                break;
                            }
                            catch (IOException ioEx)
                            {
                                attempts++;
                                AppLogger.Log($"TEMP delete attempt {attempts} failed: {ioEx.Message}. Retrying in 1s...");
                                await Task.Delay(1000);
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log("TEMP delete failed: " + ex.Message);
                                break;
                            }
                        }
                    });

                    return (true, "Print successful");
                }
                else
                {
                    AppLogger.Log("Print failed → TEMP file NOT deleted (for retry/debug).");

                    // Show MessageBox on UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Print failed: {printResult}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });

                    return (false, $"Print failed: {printResult}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Print Exception: " + ex.Message);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Print exception: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });

                return (false, $"Print exception: {ex.Message}");
            }

            finally
            {
                // Temp Image Cleanup
                if (!string.IsNullOrEmpty(resizedPath))
                {
                    try
                    {
                        if (File.Exists(resizedPath))
                        {
                            File.Delete(resizedPath);
                            AppLogger.Log("TEMP file deleted in finally.");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("TEMP delete failed in finally: " + ex.Message);
                    }
                }
            }
        }

        // Capture Payment via API
        private async Task CapturePaymentAsync(PendingOrder order)
        {
            try
            {
                var captureUrl = $"{AppConfig.ApiBaseUrl}api/payment/capture";

                //AppLogger.Log($"Capture Payment Request: {captureUrl}");

                var payload = new
                {
                    PaymentIntentId = order.PaymentIntentId,
                    ShopLoginId = order.ShopLoginId,
                    OrderId = order.OrderId
                };

                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _client.PostAsync(captureUrl, content);
                string responseText = await resp.Content.ReadAsStringAsync();

                //AppLogger.Log("Capture Payment Payload: " + responseText);
            }
            catch (Exception ex)
            {
                AppLogger.Log("CapturePayment Error: " + ex.Message);
                
            }
        }

        // Update Order Status via API
        private async Task UpdateOrderStatus(Int64 orderId, int status, string paymentIntentId, string message="", Int64? orderPaymentTypeId= null)
        {
            try
            {
                var url = $"{AppConfig.ApiBaseUrl}api/update/orderstatus?orderId={orderId}&status={status}&paymentIntentId={paymentIntentId}&message={message}&orderPaymentTypeId={orderPaymentTypeId}";
                //AppLogger.Log($"Order Status Update Request: {url}");

                await _client.GetAsync(url);

                //AppLogger.Log($"Order {orderId} updated with status {status}, paymentIntentId={paymentIntentId}");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to update status for Order {orderId}: {ex.Message}");
            }
        }

        // Update Order Status via API For Free Orders
        private async Task UpdateOrderStatusFree(Int64 orderId, int status, string message = "", Int64? orderPaymentTypeId = null)
        {
            try
            {
                var url = $"{AppConfig.ApiBaseUrl}api/update/orderstatus/free?orderId={orderId}&status={status}&message={message}&orderPaymentTypeId={orderPaymentTypeId}";
                //AppLogger.Log($"Free Order Status Update Request: {url}");

                await _client.GetAsync(url);

                //AppLogger.Log($"Free Order {orderId} updated with status {status}");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to update FREE ORDER {orderId}: {ex.Message}");
            }
        }

        // Update Printer Status via API
        private async Task UpdatePrinterStatus(long printerId, long shopLoginId, int statusCode, string message)
        {
            try
            {
                string url = $"{AppConfig.ApiBaseUrl}api/update/printerstatus" +
             $"?printerId={printerId}&shopLoginId={shopLoginId}&status={statusCode}&message={Uri.EscapeDataString(message ?? "")}";

                //AppLogger.Log("Printer Status Update Request: " + url);

                await _client.GetAsync(url);

                //AppLogger.Log($"Printer Status Updated → printerId={printerId}, shopLoginId={shopLoginId}, status={statusCode}");
            }
            catch (Exception ex)
            {
                AppLogger.Log("Printer Status Update Error: " + ex.Message);
            }
        }

        // Map Error Codes To Friendly Messages and Status Code 
        public string GetMessageFromErrorCodes(string statusText, string warningText)
        {
            try
            {
            //AppLogger.Log($"Mapping printer error codes: Status={statusText} Warning={warningText}");
            string finalMessage = "";
            bool isNotConnected =
                statusText.Contains("SDK Error 1167") ||
                statusText.Contains("0x0000048F") ||
                statusText.Contains("0x0000048f") ||
                statusText.Contains("0x000120FF"); // Not connected
            if (isNotConnected)
            {
                _printerStatus = 0;
                finalMessage = "Your printer is offline or not connected. Please check cable or power.";
            }
            else if (statusText == "Ready")
            {
                _printerStatus = 1;
                finalMessage = "Your printer is online and ready for printing.";
            }
            else if (statusText.Contains("0x00010010") ||
                     statusText.Contains("0x00010011") ||
                     statusText.Contains("0x00010012") ||
                     statusText.Contains("0x00010013") ||
                     statusText.Contains("0x00010014") ||
                     statusText.Contains("0x00010015") ||
                     statusText.Contains("0x00010016") ||
                     statusText.Contains("0x00010017") ||
                     statusText.Contains("0x00010018") ||
                     statusText.Contains("0x00010019") ||
                     statusText.Contains("0x0001001A") ||
                     statusText.Contains("0x0001001B") ||
                     statusText.Contains("0x0001001C") ||
                     statusText.Contains("0x0001001D") ||
                     statusText.Contains("0x0001001E") ||
                     statusText.Contains("0x0001001F") ||
                     statusText.Contains("0x00010091") ||
                     statusText.Contains("0x00010092"))
            {
                _printerStatus = 2;
                finalMessage = "Your printer has a card jam. Please remove the jammed card.";
            }
            else if (statusText.Contains("0x00010001"))
            {
                _printerStatus = 4;
                finalMessage = "Firmware error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010002"))
            {
                _printerStatus = 5;
                finalMessage = "TPH thermistor error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010003"))
            {
                _printerStatus = 6;
                finalMessage = "Encoder error(take). Please check the printer.";
            }
            else if (statusText.Contains("0x00010004"))
            {
                _printerStatus = 7;
                finalMessage = "Encoder error(supply). Please check the printer.";
            }
            else if (statusText.Contains("0x00010005"))
            {
                _printerStatus = 8;
                finalMessage = "ADC error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010006"))
            {
                _printerStatus = 9;
                finalMessage = "Encoder error (film). Please check the printer.";
            }
            else if (statusText.Contains("0x00010007"))
            {
                _printerStatus = 10;
                finalMessage = "Heater thermistor error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010008"))
            {
                _printerStatus = 11;
                finalMessage = "FPGA error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010009"))
            {
                _printerStatus = 12;
                finalMessage = "Burn FPGA error. Please check the printer.";
            }
            else if (statusText.Contains("0x0001000A"))
            {
                _printerStatus = 13;
                finalMessage = "Burn LCD error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010021"))
            {
                _printerStatus = 14;
                finalMessage = "Cover open. Please close the cover.";
            }
            else if (statusText.Contains("0x00010022"))
            {
                _printerStatus = 15;
                finalMessage = "Rejectbox open. Please close the rejectbox.";
            }
            else if (statusText.Contains("0x00010023"))
            {
                _printerStatus = 16;
                finalMessage = "Rejectbox full. Please empty the rejectbox.";
            }
            else if (statusText.Contains("0x00010024"))
            {
                _printerStatus = 17;
                finalMessage = "Flat cover open. Please close the flat cover.";
            }
            else if (statusText.Contains("0x00010031"))
            {
                _printerStatus = 18;
                finalMessage = "TPH cam error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010032"))
            {
                _printerStatus = 19;
                finalMessage = "ADF cam error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010033"))
            {
                _printerStatus = 20;
                finalMessage = "Flipper error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010034"))
            {
                _printerStatus = 21;
                finalMessage = "Heater cam error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010035"))
            {
                _printerStatus = 22;
                finalMessage = "Flat cam error. Please check the printer.";
            }
            else if (statusText.Contains("0x00010041") ||
                     statusText.Contains("0x00010046"))
            {
                _printerStatus = 23;
                finalMessage = "Ribbon out. Please install a new ribbon.";
            }
            else if (statusText.Contains("0x00010042") ||
                     statusText.Contains("0x00010048") ||
                     statusText.Contains("0x00010049"))
            {
                _printerStatus = 24;
                finalMessage = "Ribbon error. Please check the ribbon.";
            }
            else if (statusText.Contains("0x00010043") ||
                     statusText.Contains("0x00010045"))
            {
                _printerStatus = 25;
                finalMessage = "Ribbon missing. Please install the ribbon.";
            }
            else if (statusText.Contains("0x00010044"))
            {
                _printerStatus = 26;
                finalMessage = "Ribbon unsupported. Please use a supported ribbon.";
            }
            else if (statusText.Contains("0x00010047"))
            {
                _printerStatus = 27;
                finalMessage = "Ribbon mismatch. Please check ribbon compatibility.";
            }
            else if (statusText.Contains("0x0001004A"))
            {
                _printerStatus = 28;
                finalMessage = "Ribbon installation error. Please reinstall the ribbon.";
            }
            else if (statusText.Contains("0x00010051") ||
                     statusText.Contains("0x00010052") ||
                     statusText.Contains("0x00010053"))
            {
                _printerStatus = 29;
                finalMessage = "Card feed error.";
            }
            else if (statusText.Contains("0x0001005E"))
            {
                _printerStatus = 30;
                finalMessage = "Card eject error. Please remove the card manually.";
            }
            else if (statusText.Contains("0x0001005F"))
            {
                _printerStatus = 31;
                finalMessage = "Card out. No card detected.";
            }
            else if (statusText.Contains("0x00010061") ||
                     statusText.Contains("0x00010062") ||
                     statusText.Contains("0x00010063"))
            {
                _printerStatus = 32;
                finalMessage = "Authenticate error. Please check authentication settings.";
            }
            else if (statusText.Contains("0x00010071") ||
                     statusText.Contains("0x00010076"))
            {
                _printerStatus = 33;
                finalMessage = "Film out. Please install new film.";
            }
            else if (statusText.Contains("0x00010072") ||
                     statusText.Contains("0x00010077") ||
                     statusText.Contains("0x00010078"))
            {
                _printerStatus = 34;
                finalMessage = "Film error. Please check the film.";
            }
            else if (statusText.Contains("0x00010073") ||
                     statusText.Contains("0x00010075"))
            {
                _printerStatus = 35;
                finalMessage = "Film missing. Please install the film.";
            }
            else if (statusText.Contains("0x00010074") ||
                     statusText.Contains("0x00010079"))
            {
                _printerStatus = 36;
                finalMessage = "Film unsupported. Please use supported film.";
            }
            else if (statusText.Contains("0x0001007A"))
            {
                _printerStatus = 37;
                finalMessage = "Film installation error. Please reinstall the film.";
            }
            else if (statusText.Contains("0x00010081"))
            {
                _printerStatus = 38;
                finalMessage = "ADF module error. Please check the ADF module.";
            }
            else if (statusText.Contains("0x00010082"))
            {
                _printerStatus = 39;
                finalMessage = "Indent-printing error. Please check indent-printing module.";
            }
            else if (statusText.Contains("0x00012001"))
            {
                _printerStatus = 40;
                finalMessage = "System busy. Please wait and try again.";
            }
            else if (statusText.Contains("0x00012002"))
            {
                _printerStatus = 41;
                finalMessage = "Invalid command. Please check the command.";
            }
            else if (statusText.Contains("0x00012003"))
            {
                _printerStatus = 42;
                finalMessage = "Parameter error. Please check parameters.";
            }
            else if (statusText.Contains("0x00012004"))
            {
                _printerStatus = 43;
                finalMessage = "Validation failed. Please check input data.";
            }
            else if (statusText.Contains("0x00012005"))
            {
                _printerStatus = 44;
                finalMessage = "Read-only data. Cannot modify read-only data.";
            }
            else if (statusText.Contains("0x00012006"))
            {
                _printerStatus = 45;
                finalMessage = "Card inside. Please remove the card.";
            }
            else if (statusText.Contains("0x00012007"))
            {
                _printerStatus = 46;
                finalMessage = "No card inside. Please insert a card.";
            }
            else if (statusText.Contains("0x00012008"))
            {
                _printerStatus = 47;
                finalMessage = "The pressing mechanism is not at the origin. Please check the mechanism.";
            }
            else if (statusText.Contains("0x00012009"))
            {
                _printerStatus = 48;
                finalMessage = "Card input or card output timeout. Please check card handling.";
            }
            else if (statusText.Contains("0x0001200A"))
            {
                _printerStatus = 49;
                finalMessage = "Ribbon cassette not locked. Please lock the ribbon cassette.";
            }
            else if (statusText.Contains("0x0001200B"))
            {
                _printerStatus = 50;
                finalMessage = "Ribbon cassette not detected. Please check ribbon installation.";
            }
            else if (statusText.Contains("0x0001200C"))
            {
                _printerStatus = 51;
                finalMessage = "RFID detection failed. Please check RFID.";
            }
            else if (statusText.Contains("0x0001200D"))
            {
                _printerStatus = 52;
                finalMessage = "Ribbon tension timeout. Please check ribbon tension.";
            }
            else if (statusText.Contains("0x0001200E"))
            {
                _printerStatus = 53;
                finalMessage = "Dial positioning error. Please check dial position.";
            }
            else if (statusText.Contains("0x0001200F"))
            {
                _printerStatus = 54;
                finalMessage = "Press down timeout. Please check pressing mechanism.";
            }
            else if (statusText.Contains("0x00012010"))
            {
                _printerStatus = 55;
                finalMessage = "RFID cardless. RFID card required.";
            }
            else if (statusText.Contains("0x00012011"))
            {
                _printerStatus = 56;
                finalMessage = "RFID key error. Please check RFID key.";
            }
            else if (statusText.Contains("0x00012012"))
            {
                _printerStatus = 57;
                finalMessage = "Failed to read and write RFID data. Please check RFID card.";
            }
            else if (statusText.Contains("0x00012013"))
            {
                _printerStatus = 58;
                finalMessage = "Abnormal RFID card. Please use a valid RFID card.";
            }
            else if (statusText.Contains("0x00012014"))
            {
                _printerStatus = 59;
                finalMessage = "Abnormal down pressure. Please check pressure settings.";
            }
            else if (statusText.Contains("0x00012015"))
            {
                _printerStatus = 60;
                finalMessage = "Abnormal ribbon. Please check ribbon.";
            }
            else if (statusText.Contains("0x00012016"))
            {
                _printerStatus = 61;
                finalMessage = "Abnormal digital disk. Please check digital disk.";
            }
            else if (statusText.Contains("0x00012017"))
            {
                _printerStatus = 62;
                finalMessage = "Abnormal card entry. Please check card insertion.";
            }
            else if (statusText.Contains("0x00012018"))
            {
                _printerStatus = 63;
                finalMessage = "Insufficient Ribbon. Please replace ribbon soon.";
            }
            else if (statusText.Contains("0x000120FD"))
            {
                _printerStatus = 64;
                finalMessage = "UART error. Please check connection.";
            }
            else if (statusText.Contains("0x000120FE"))
            {
                _printerStatus = 65;
                finalMessage = "Bootloader mode. Printer in bootloader mode.";
            }
            else if (statusText.Contains("0x000100A1"))
            {
                _printerStatus = 66;
                finalMessage = "The firmware command is not supported. Please update firmware.";
            }
            else if (statusText.Contains("0x000100A2"))
            {
                _printerStatus = 67;
                finalMessage = "The firmware command parameter is wrong. Please check parameters.";
            }
            else if (statusText.Contains("0x000100A3"))
            {
                _printerStatus = 68;
                finalMessage = "The firmware task is not created. Please restart printer.";
            }
            else if (statusText.Contains("0x000100A4"))
            {
                _printerStatus = 69;
                finalMessage = "The firmware command is rejected. Please check command.";
            }
            else if (statusText.Contains("0x000100A5"))
            {
                _printerStatus = 70;
                finalMessage = "The printer memory is full. Please clear memory.";
            }
            else if (statusText.Contains("0x000100A6"))
            {
                _printerStatus = 71;
                finalMessage = "Need authentication before processing command. Please authenticate.";
            }
            else if (statusText.Contains("0x000100A7"))
            {
                _printerStatus = 72;
                finalMessage = "Fail to update firmware. Please try updating again.";
            }
            else if (statusText.Contains("0x00011001"))
            {
                _printerStatus = 73;
                finalMessage = "Track data is empty. Please provide track data.";
            }
            else if (statusText.Contains("0x00011002"))
            {
                _printerStatus = 74;
                finalMessage = "There is wrong character in track 1. Please check track 1 data.";
            }
            else if (statusText.Contains("0x00011003"))
            {
                _printerStatus = 75;
                finalMessage = "There is wrong character in track 2. Please check track 2 data.";
            }
            else if (statusText.Contains("0x00011004"))
            {
                _printerStatus = 76;
                finalMessage = "There is wrong character in track 3. Please check track 3 data.";
            }
            else if (statusText.Contains("0x00011005"))
            {
                _printerStatus = 77;
                finalMessage = "Magnetic encoding module is not attached. Please attach module.";
            }
            else if (statusText.Contains("0x00011006"))
            {
                _printerStatus = 78;
                finalMessage = "Encoding magnetic stripe fail! Please check encoding.";
            }
            else if (statusText.Contains("0x00011007"))
            {
                _printerStatus = 79;
                finalMessage = "The flipper module is not attached. Please attach flipper module.";
            }
            else if (statusText.Contains("0x00011008"))
            {
                _printerStatus = 80;
                finalMessage = "The 600DPI feature is not enabled. Please enable 600DPI.";
            }
            else if (statusText.Contains("0x00011009"))
            {
                _printerStatus = 81;
                finalMessage = "Track 1 is empty. Please provide track 1 data.";
            }
            else if (statusText.Contains("0x0001100A"))
            {
                _printerStatus = 82;
                finalMessage = "Track 2 is empty. Please provide track 2 data.";
            }
            else if (statusText.Contains("0x0001100B"))
            {
                _printerStatus = 83;
                finalMessage = "Track 3 is empty. Please provide track 3 data.";
            }
            else if (statusText.Contains("0x0001100C"))
            {
                _printerStatus = 84;
                finalMessage = "Track 1,2 are empty. Please provide track data.";
            }
            else if (statusText.Contains("0x0001100D"))
            {
                _printerStatus = 85;
                finalMessage = "Track 1,3 are empty. Please provide track data.";
            }
            else if (statusText.Contains("0x0001100E"))
            {
                _printerStatus = 86;
                finalMessage = "Track 2,3 are empty. Please provide track data.";
            }
            else if (statusText.Contains("0x0001100F"))
            {
                _printerStatus = 87;
                finalMessage = "Track 1,2,3 are empty. Please provide track data.";
            }
            else if (statusText.Contains("0x00011011"))
            {
                _printerStatus = 88;
                finalMessage = "Ribbon is not matched setting. Please check ribbon settings.";
            }
            else if (statusText.Contains("0x00011012"))
            {
                _printerStatus = 89;
                finalMessage = "The feature of printing non-standard length card is not enabled. Please enable feature.";
            }
            else if (statusText.Contains("0x00011013"))
            {
                _printerStatus = 90;
                finalMessage = "The drawing text or image is outside the scope of the card. Please adjust design.";
            }
            else if (statusText.Contains("0x00011014"))
            {
                _printerStatus = 91;
                finalMessage = "There is no card in printer. Please insert a card.";
            }
            else if (statusText.Contains("0x00011015"))
            {
                _printerStatus = 92;
                finalMessage = "Please take out ribbon.";
            }
            else if (statusText.Contains("0x00011020"))
            {
                _printerStatus = 93;
                finalMessage = "Magstripe is blank. Please encode magstripe.";
            }
            else if (statusText.Contains("0x00011021"))
            {
                _printerStatus = 94;
                finalMessage = "Track 1 data is blank or has errors. Please check track 1.";
            }
            else if (statusText.Contains("0x00011022"))
            {
                _printerStatus = 95;
                finalMessage = "Track 2 data is blank or has errors. Please check track 2.";
            }
            else if (statusText.Contains("0x00011023"))
            {
                _printerStatus = 96;
                finalMessage = "Track 3 data is blank or has errors. Please check track 3.";
            }
            else if (statusText.Contains("0x00011024"))
            {
                _printerStatus = 97;
                finalMessage = "Track 1,2 data is blank or has errors. Please check tracks.";
            }
            else if (statusText.Contains("0x00011025"))
            {
                _printerStatus = 98;
                finalMessage = "Track 1,3 data is blank or has errors. Please check tracks.";
            }
            else if (statusText.Contains("0x00011026"))
            {
                _printerStatus = 99;
                finalMessage = "Track 2,3 data is blank or has errors. Please check tracks.";
            }
            else if (statusText.Contains("0x00011027"))
            {
                _printerStatus = 100;
                finalMessage = "Track 1,2,3 data is blank or has errors. Please check tracks.";
            }
            else if (statusText.Contains("0x00011030"))
            {
                _printerStatus = 101;
                finalMessage = "The smart card module is not attached. Please attach smart card module.";
            }
            else if (statusText.Contains("0x00011031"))
            {
                _printerStatus = 102;
                finalMessage = "Calibrate smart card position fail. Please recalibrate.";
            }
            else if (statusText.Contains("0x00011040"))
            {
                _printerStatus = 103;
                finalMessage = "The firmware file does not match the printer model. Please use correct firmware.";
            }
            else if (statusText.Contains("0x00011041"))
            {
                _printerStatus = 104;
                finalMessage = "The current firmware version is the same as the firmware file. No update needed.";
            }
            else if (statusText.Contains("0x00011042"))
            {
                _printerStatus = 105;
                finalMessage = "The current firmware version is newer than firmware file. No update needed.";
            }
            else if (statusText.Contains("0x00011043"))
            {
                _printerStatus = 106;
                finalMessage = "Indent-printing module is not attached. Please attach module.";
            }
            else if (statusText.Contains("0x00013001"))
            {
                _printerStatus = 107;
                finalMessage = "Printer is doing ribbon security erase. Please wait.";
            }
            else if (statusText.Contains("0x00013002"))
            {
                _printerStatus = 108;
                finalMessage = "Printing job is suspended. Please resume job.";
            }
            else if (statusText.Contains("0x00013003"))
            {
                _printerStatus = 109;
                finalMessage = "There is one card in storage position. Please remove if needed.";
            }
            else if (statusText.Contains("0x00013004"))
            {
                _printerStatus = 110;
                finalMessage = "There is no card in storage position.";
            }
            else if (statusText.Contains("0x00013005"))
            {
                _printerStatus = 111;
                finalMessage = "Printer is locked. Please unlock the printer.";
            }
            else if (statusText.Contains("0x000000AA"))
            {
                //_printerStatus = 112;
                //finalMessage = "Printer is in Use.";

                //Treat as Ready When Printer is in Use Error Occured.
                _printerStatus = 1;
                finalMessage = "Your printer is online and ready for printing.";
            }
                else if (statusText.Contains("Printer Error"))
            {
                _printerStatus = 3;
                finalMessage = "Your printer has an error. Please check the printer.";
            }

            // WARNINGS (These DO NOT affect status code)
            if (!string.IsNullOrEmpty(warningText))
            {
                if (warningText.Contains("0x00020001"))
                    finalMessage += "\n\nAdditional Info: Ribbon is low.";
                else if (warningText.Contains("0x00020004"))
                    finalMessage += "\n\nAdditional Info: Card tray is almost empty.";
                else if (warningText.StartsWith("SDK Error"))
                    finalMessage += $"\n\n{warningText}";
            }
            //AppLogger.Log($"Final Printer Message: {finalMessage}");
            return finalMessage.Trim();
        }
    catch (Exception ex)
    {
        AppLogger.Log($"Error in GetMessageFromErrorCodes: {ex.Message}");

        _printerStatus = 3;

        return "Unable to read printer status. Please check the printer connection.";
    }
}

        //Make Printer Offline Printer Id ShopLogin Id
        public static async Task SetPrinterOffline(long printerId, long shopLoginId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"{AppConfig.ApiBaseUrl}api/update/printerstatus/offline?printerId={printerId}&shopLoginId={shopLoginId}";

                    //AppLogger.Log("Calling Printer Offline API: " + url);

                    await client.GetAsync(url);

                    //AppLogger.Log("Printer offline status updated.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("SetPrinterOffline Error: " + ex.Message);
            }
        }

        // Function To Handle Print Error Message
        private async Task<(bool, string)> WaitForPrintCompletionAsync(string printerName)
        {
            try
            {
            const int MAX_WAIT = 120; // seconds
            bool feedDetected = false;
            int readyStableCount = 0;

            for (int i = 0; i < MAX_WAIT; i++)
            {
                await Task.Delay(1000);

                string status = SeaoryPrinterPrint.CheckPrinterStatus(printerName);
                string warning = SeaoryPrinterPrint.CheckPrinterWarning(printerName);

                //AppLogger.Log($"[SEAORY-MONITOR] Status={status}, Warning={warning}");

                // SDK error
                if (status.StartsWith("SDK Error"))
                    return (false, status);

                // CARD FEED / PRINTING
                if (status.Contains("0x000000AA") || warning.Contains("0x000000AA"))
                {
                    feedDetected = true;
                    readyStableCount = 0;
                    continue;
                }

                // CARD FEED ERROR / MECHANICAL ERROR
                if (status.Contains("0x") && !status.Contains("0x00000000"))
                {
                    string err = GetMessageFromErrorCodes(status, warning);
                    return (false, err ?? "Printer hardware error");
                }

                // 🟢 READY handling
                if (status == "Ready")
                {
                    if (feedDetected)
                    {
                        readyStableCount++;

                        // READY stable for 3 seconds → real success
                        if (readyStableCount >= 3)
                            return (true, "Print completed successfully");
                    }
                    continue;
                }
            }

            return (false, "Print timeout or printer did not complete card movement");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error in WaitForPrintCompletionAsync: {ex.Message}");
            return (false, $"Exception occurred: {ex.Message}");
        }
}

        // Refund TWINT Payment via API
        public async Task<bool> RefundTwintPaymentAsync(PendingOrder order, decimal refundAmount)
        {
            try
            {
                var refundUrl = $"{AppConfig.ApiBaseUrl}api/payment/twint/refund";

                var payload = new
                {
                    PaymentIntentId = order.PaymentIntentId,
                    ShopLoginId = order.ShopLoginId,
                    RefundAmount = refundAmount
                };

                string json = JsonConvert.SerializeObject(payload);

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _client.PostAsync(refundUrl, content);

                string responseText = await resp.Content.ReadAsStringAsync();


                // Success only if HTTP 200-299
                if (resp.IsSuccessStatusCode)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(responseText))
                        {
                            dynamic? result = JsonConvert.DeserializeObject(responseText);
                            if (result != null)
                            {
                                // Check 'status'
                                if (result.status != null)
                                {
                                    string statusStr = result.status.ToString().ToLower();
                                    if (statusStr == "1" || statusStr == "true")
                                    {
                                        return true;
                                    }
                                    return false;
                                }
                                // Check 'success'
                                if (result.success != null)
                                {
                                    string successStr = result.success.ToString().ToLower();
                                    if (successStr == "1" || successStr == "true")
                                    {
                                        return true;
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        AppLogger.Log("Error parsing refund response JSON: " + jsonEx.Message);
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Log("RefundTwintPayment Error: " + ex);

                return false;
            }
        }

        // Process Single Order
        public async Task<bool> ProcessSingleOrderAsync(PendingOrder order, bool isRetry = false)
        {
            try
            {
                //AppLogger.Log(isRetry
                //    ? $"Retrying failed Order #{order.OrderId}"
                //    : $"Processing Order #{order.OrderId}");

                // PRINT PROGRESS UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    NavigationService.Navigate(new PrintProgressView());
                });

                var (isSuccess, message) = await PrintOrderAsync(order);

                if (isSuccess)
                {
                    // Wait for timer completion in PrintProgressView
                    while (!(NavigationService.GetCurrentPage() is PrintProgressView printProgressView && printProgressView.IsTimerCompleted()))
                    {
                        await Task.Delay(500);  // Wait until the timer completes
                    }

                    if (order.IsPaidOrder == 1)
                    {
                        if (order.OrderPaymentTypeId == 1) // Stripe
                            await CapturePaymentAsync(order);

                        // For Stripe
                        await UpdateOrderStatus(order.OrderId, 1, order.PaymentIntentId ?? string.Empty, orderPaymentTypeId: order.OrderPaymentTypeId); // success
                    }
                    else
                    {
                        //Free
                        await UpdateOrderStatusFree(order.OrderId, 1, orderPaymentTypeId: order.OrderPaymentTypeId); // success
                    }

                    //Application.Current.Dispatcher.Invoke(() =>
                    //{
                    //    NavigationService.Navigate(new PrintSuccessView());
                    //});

                    //await Task.Delay(5000);
                    //NavigateBackToScanQR();

                    //AppLogger.Log($"[STUCK-DEBUG] Order #{order.OrderId} — Navigating to PrintSuccessView");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        NavigationService.Navigate(new PrintSuccessView());
                    });

                    //AppLogger.Log($"[STUCK-DEBUG] Order #{order.OrderId} — PrintSuccessView navigated, starting 5s delay");

                    await Task.Delay(5000);

                    //AppLogger.Log($"[STUCK-DEBUG] Order #{order.OrderId} — 5s delay completed, calling NavigateBackToScanQR");

                    NavigateBackToScanQR();

                    //AppLogger.Log($"[STUCK-DEBUG] Order #{order.OrderId} — NavigateBackToScanQR call finished");


                    return true; // Print Success
                }
                else
                {
                    await HandlePrintFailure(order, message);
                    return false; // print Fail
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProcessSingleOrderAsync Exception: {ex}");
                return false; // Exception occured 
            }
        }

        // Handle Print Failure
        private async Task HandlePrintFailure(PendingOrder order, string message)
        {
            try
            {
                FailedPrintSession.Add(order);

                if (order.IsPaidOrder == 1 && (order.OrderPaymentTypeId == 1 || order.OrderPaymentTypeId == 3)) // OrderPaymentTypeId = 1 For Stripe or 3 for Twint
                {
                    await UpdateOrderStatus(order.OrderId, 2, order.PaymentIntentId ?? "", message, order.OrderPaymentTypeId); // failed
                }
                else
                {
                    // Free Order
                    await UpdateOrderStatusFree(order.OrderId, 2, message, order.OrderPaymentTypeId); // failed
                }

                // Send Email on Print Fail and Update Status
                await UpdatePrinterStatus(_printerId, _shopLoginId, _printerStatus, message);

                // Immediately stop timer and navigate to error view on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Stop the timer in PrintProgressView if it's the current page
                    var currentPage = NavigationService.GetCurrentPage();

                    AppLogger.Log($"[DEBUG] HandlePrintFailure — current page before navigating to error view: {currentPage?.GetType().Name ?? "null"}");

                    if (currentPage is PrintProgressView printProgressView)
                    {
                        printProgressView.StopTimer();

                        AppLogger.Log("[DEBUG] HandlePrintFailure - Stopped PrintProgressView timer due to print failure.");
                    }

                    AppLogger.Log($"[DEBUG] HandlePrintFailure — Navigating to PrintErrorView with message: {message}");

                    NavigationService.Navigate(new PrintErrorView(message, order, order.OrderPaymentTypeId));
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error in HandlePrintFailure: {ex.Message}");
            }
        }

        // Navigate Back to Scan QR View
        private void NavigateBackToScanQR()
        {
            //AppLogger.Log("[STUCK-DEBUG] NavigateBackToScanQR — entering Dispatcher.Invoke");

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Avoid duplicate navigation
                    var currentPage = NavigationService.GetCurrentPage();

                    //AppLogger.Log($"[STUCK-DEBUG] NavigateBackToScanQR — current page: {currentPage?.GetType().Name ?? "null"}");

                    if (currentPage is ScanQrView)
                    {
                        //AppLogger.Log("[STUCK-DEBUG] NavigateBackToScanQR — already on ScanQrView, skipping");
                        return;
                    }

                    Helpers.NavigationService.Navigate(
                        new ScanQrView(
                            _jwtToken,
                            _shopLoginId,
                            _printerId,
                            _printerName,
                            _qrCodeData,
                            _printerPrice,
                            _printerStatus,
                            _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                            _printerPaymentEnabled
                        )
                    );

                    //AppLogger.Log("[STUCK-DEBUG] NavigateBackToScanQR — ScanQrView navigation done successfully");

                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[STUCK-DEBUG] NavigateBackToScanQR ERROR: {ex}");
                }
            });

            AppLogger.Log("[STUCK-DEBUG] NavigateBackToScanQR — Dispatcher.Invoke completed");

        }

        // Track Print Failure via API -- Refund Order Tracking
        public async Task TrackPrintFailureAsync(PendingOrder order)
        {
            try
            {
                var payload = new
                {
                    ShopLoginId = order.ShopLoginId,
                    OrderId = order.OrderId,
                    PaymentIntentId = order.PaymentIntentId,
                    OrderPaymentTypeId = order.OrderPaymentTypeId
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"{AppConfig.ApiBaseUrl}api/refund/track-print-failure",content);

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"TrackPrintFailure API failed. Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error in TrackPrintFailureAsync: {ex.Message}");
            }
        }
    }
}

