using com.epson.label.driver;
using Microsoft.Identity.Client;
using kuboPix.Helpers;
using kuboPix.ViewModels;
using kuboPix.Views;
using kuboPix.Wrapper;
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

namespace kuboPix.Services
{
    public class OrderQueueService : IDisposable
    {
        // Fields
        private readonly string _jwtToken;
        private readonly Int64 _shopLoginId;
        private readonly Int64 _printerId;
        private readonly string _printerName;
        private readonly string _printerBrand;
        private readonly HttpClient _client;

        // SemaphoreSlim(1,1) used instead of a plain bool so the "is running" check
        // is atomic — a non-volatile bool can be read as false by two threads simultaneously.
        private readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(1, 1);

        private bool _isStarted = false;

        private CancellationTokenSource _cts = new CancellationTokenSource();

        // Background loop handles + disposal coordination (T-4.2)
        private Task? _queueLoopTask;
        private Task? _monitorLoopTask;
        private bool _disposed = false;
        private readonly object _disposeLock = new object();

        private int _printerStatus = 0;
        private string _printerMessage = "";

        private string _qrCodeData;
        private decimal _printerPrice;
        private DateTime _qrExpiry;

        private int _printerPaymentEnabled;

        private static HashSet<long> _processedOrderIds = new HashSet<long>();

        // Guards the check-then-claim of an order so two threads/instances can't both claim it (T-5.2).
        private static readonly object _processedLock = new object();

        // Seed the in-memory session set from the persistent printed-ledger on first use,
        // so orders printed before a restart are not picked up and printed again (T-5.1).
        static OrderQueueService()
        {
            try
            {
                foreach (var id in ProcessedOrderLedger.All())
                    _processedOrderIds.Add(id);
            }
            catch (Exception ex)
            {
                AppLogger.Log("ProcessedOrderLedger seed error: " + ex.Message);
            }
        }

        public OrderQueueService(string jwtToken, long shopLoginId, long printerId, string printerName, int printerStatus, string qrCodeData, decimal printerPrice, DateTime qrExpiry, int printerPaymentEnabled, string printerBrand = "Seaory")
        {
            _jwtToken = jwtToken;
            _shopLoginId = shopLoginId;
            _printerId = printerId;
            _printerName = printerName;
            _printerStatus = printerStatus;
            _printerBrand = string.IsNullOrEmpty(printerBrand) ? (LoginView.SelectedPrinterBrand ?? "Seaory") : printerBrand;
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
            _queueLoopTask = Task.Run(async () =>
            {
                // T-5.4 startup reconcile: push any "printed but not yet acknowledged" status
                // updates BEFORE polling, so the backend stops reporting those orders as pending.
                try { await FlushPendingStatusUpdatesAsync(); }
                catch (Exception ex) { AppLogger.Log("Startup reconcile error: " + ex.Message); }

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Retry any queued status updates each cycle (T-5.3).
                        await FlushPendingStatusUpdatesAsync();

                        if (await _processSemaphore.WaitAsync(0))
                        {
                            try { await ProcessPendingOrders(); }
                            finally { _processSemaphore.Release(); }
                        }
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
            _monitorLoopTask = StartPrinterMonitoring();

            // Release CTS / HttpClient / semaphore once BOTH background loops have fully
            // exited (i.e. after StopQueue cancels them). Disposing only after the loops
            // finish guarantees no in-flight request touches a disposed client (T-4.2).
            _ = Task.WhenAll(_queueLoopTask, _monitorLoopTask)
                    .ContinueWith(_ => DisposeResources(), TaskScheduler.Default);
        }

        // Stop the queue and monitoring (useful for cleanup)
        public void StopQueue()
        {
            try
            {
                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    AppLogger.Log("Queue stopped.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StopQueue error: " + ex.Message);
            }
        }

        // IDisposable — cancel the loops; actual resource disposal happens once both loops
        // have exited (via the WhenAll continuation in StartQueue). If the queue was never
        // started (e.g. a service used only for refund/track), dispose immediately (T-4.2).
        public void Dispose()
        {
            StopQueue();

            if (!_isStarted)
                DisposeResources();
        }

        // Disposes shared resources exactly once. Safe to call from the loop-completion
        // continuation or directly from Dispose() when no loops were started.
        //
        // NOTE: _client is intentionally NOT disposed here. The manual-retry path
        // (ProcessSingleOrderAsync) runs OUTSIDE the queue loop and shares _client; when it
        // navigates to PrintProgressView the page unloads → StopQueue cancels the (idle) loops
        // → this runs. Disposing _client here would pull it out from under the in-flight retry
        // and make the image download throw, surfacing a misleading "image expired" error.
        // HttpClient is designed to be long-lived; leaving it to GC is the correct pattern.
        private void DisposeResources()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try { _cts.Dispose(); } catch { }
            try { _processSemaphore.Dispose(); } catch { }
        }

        // Monitor printer status in background (debounced). Returns the loop Task so the
        // caller can await its completion before disposing shared resources (T-4.2).
        private Task StartPrinterMonitoring()
        {
            //AppLogger.Log("Printer monitoring started.");

            return Task.Run(async () =>
            {
                int consecReady = 0;
                int consecNotReady = 0;
                const int requiredConsecutive = 3; // require 3 consistent reads
                int previousStatus = _printerStatus; // <-- 1. Moved it outside the loop (saves the previous status).

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (_printerBrand == "Epson")
                        {
                            StatusCode sc = StatusCode.ST_Idle;
                            ErrorCode ec = ErrorCode.ERR_FatalError;
                            WARNING_INFO wc = new WARNING_INFO();
                            bool queryOk = false;
                            ENS2Control ensControl = ENS2Control.GetInstance();
                            try
                            {

                                ensControl.Initialize(_printerName, "");
                                sc = ensControl.GetStatusInformation();
                                ec = ensControl.GetErrorInformation();
                                wc = ensControl.GetWarningInformation();

                                queryOk = true;
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log("Epson Monitor Status query exception: " + ex.Message);
                            }
                            finally
                            {
                                try { ensControl.Release(); } catch { }
                            }
                            if (queryOk)
                            {
                                string parsedMessage = GetEpsonMessageFromErrorCodes(sc, ec, wc);
                                bool isReadyNow = sc == StatusCode.ST_Idle;

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
                                    GetEpsonMessageFromErrorCodes(sc, ec, wc);
                                    _printerMessage = parsedMessage;
                                }
                                else
                                {
                                    // Do not change _printerStatus yet
                                    _printerMessage = parsedMessage;
                                }
                            }
                            else
                            {
                                consecNotReady++;
                                consecReady = 0;
                                if (consecNotReady >= requiredConsecutive)
                                {
                                    _printerStatus = 0; // offline
                                    _printerMessage = "Your printer is offline or not connected. Please check cable or power.";
                                }
                            }
                        }
                        else // Seaory
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
                //AppLogger.Log("Checking for pending orders...");

                var pendingOrders = await GetPendingOrders();
                //AppLogger.Log($"Pending Orders Retrieved: {pendingOrders.Count}");

                // Filter: skip orders already claimed this session OR already printed
                // (persistent ledger — survives restart).
                var newOrders = pendingOrders
                    .Where(o => !_processedOrderIds.Contains(o.OrderId)
                                && !ProcessedOrderLedger.IsPrinted(o.OrderId))
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

                    // Atomic check-then-claim (T-5.2). Done BEFORE navigating so a duplicate /
                    // already-printed order is skipped without leaving a stale PrintProgressView
                    // on the navigation stack (the 26-minute freeze bug). The lock makes the
                    // "is it claimed?" decision and the claim a single operation, so a duplicate
                    // order — or a second concurrent loop — can never both pass and print twice.
                    // The persistent printed-ledger check means a restart cannot reprint either.
                    bool alreadyHandled;
                    lock (_processedLock)
                    {
                        alreadyHandled = _processedOrderIds.Contains(order.OrderId)
                                         || ProcessedOrderLedger.IsPrinted(order.OrderId);
                        if (!alreadyHandled)
                            _processedOrderIds.Add(order.OrderId); // claim
                    }

                    if (alreadyHandled)
                    {
                        AppLogger.Log($"This Order is Already Processed {order.OrderId}");
                        continue;
                    }

                    PrintProgressView? printProgressView = null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        printProgressView = new PrintProgressView();
                        NavigationService.Navigate(printProgressView);  // Set the current page as PrintProgressView
                    });

                    var processedIdsString = string.Join(", ", _processedOrderIds);
                    //AppLogger.Log($"Processed Order Ids First: {processedIdsString}");

                    var (isSuccess, message) = await PrintOrderAsync(order);

                    //AppLogger.Log($"Success: {isSuccess}, Message: {message}");

                    if (isSuccess)
                    {
                        // Record the successful HARDWARE print to the persistent ledger BEFORE
                        // any status-update API call, so a crash / outage / restart between the
                        // print and the status update can never cause a reprint (T-5.1).
                        ProcessedOrderLedger.MarkPrinted(order.OrderId);

                        //AppLogger.Log($"Print success for Order {order.OrderId}");

                        if (printProgressView != null)
                        {
                            try
                            {
                                await printProgressView.WaitForCompletionAsync();
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log($"PrintProgress wait failed: {ex}");
                            }
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

                            // Wait 5 seconds, then return to CaptureView
                            await Task.Delay(5000);

                            //AppLogger.Log($"[STUCK-DEBUG] PAID Order #{order.OrderId} — 5s delay done, navigating to CaptureView");

                            NavigateBackToCapture();

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

                            //AppLogger.Log($"[STUCK-DEBUG] FREE Order #{order.OrderId} — 5s delay done, navigating to CaptureView");

                            NavigateBackToCapture();
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

        // Resize Image to CR80 (1011x637px @ 300 DPI for Seaory, 2017x1276px @ 600 DPI for Epson) - with orientation lock        
        private string ResizeAndCropToCard(Image img)
        {
            try
            {

                //AppLogger.Log("Resizing image to CR80 card size...");

                int cardWidthPx = _printerBrand == "Epson" ? 2017 : 1011;
                int cardHeightPx = _printerBrand == "Epson" ? 1276 : 637;
                const int Margin = 2;

                // Ensure landscape orientation before processing
                if (img.Height > img.Width)
                {
                    img.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    AppLogger.Log("Image rotated to landscape.");
                }

                double cardRatio = (double)cardWidthPx / cardHeightPx;
                double imgRatio = (double)img.Width / img.Height;

                int newWidth, newHeight;

                if (imgRatio > cardRatio)
                {
                    newHeight = cardHeightPx;
                    newWidth = (int)Math.Round(imgRatio * cardHeightPx);
                }
                else
                {
                    newWidth = cardWidthPx;
                    newHeight = (int)Math.Round(cardWidthPx / imgRatio);
                }

                using (var bmp = new Bitmap(newWidth, newHeight))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.DrawImage(img, 0, 0, newWidth, newHeight);

                    Rectangle cropRect = new Rectangle(
                       Math.Max(0, (newWidth - cardWidthPx) / 2),
                        Math.Max(0, (newHeight - cardHeightPx) / 2),
                        cardWidthPx - Margin,
                        cardHeightPx - Margin
                    );

                    using (var finalBmp = bmp.Clone(cropRect, bmp.PixelFormat))
                    {
                        string ext = _printerBrand == "Epson" ? "png" : "jpg";
                        string tempFile = Path.Combine(Path.GetTempPath(),
   $"{_printerBrand}_PrintReady_" + Guid.NewGuid() + "." + ext);


                        int dpi = _printerBrand == "Epson" ? 600 : 300;
                        finalBmp.SetResolution(dpi, dpi);
                        var format = _printerBrand == "Epson" ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg;
                        finalBmp.Save(tempFile, format);
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

                string printResult = "";
                bool finalSuccess = false;
                string finalError = "";

                if (_printerBrand == "Epson")
                {
                    ENS2Control ensControl = ENS2Control.GetInstance();
                    try
                    {

                        ensControl.Initialize(_printerName, "");
                        StatusCode sc = ensControl.GetStatusInformation();
                        ErrorCode ec = ensControl.GetErrorInformation();
                        WARNING_INFO wc = ensControl.GetWarningInformation();


                        string parsedMessage = GetEpsonMessageFromErrorCodes(sc, ec, wc);
                        if (sc != StatusCode.ST_Idle)
                        {
                            AppLogger.Log($"Epson Printer not ready. Status: {sc}, Error: {ec}");
                            return (false, parsedMessage);
                        }
                    }
                    catch (DllNotFoundException dllEx)
                    {
                        AppLogger.Log($"Epson SDK DLL Load Failed: {dllEx.Message}. Proceeding without status check.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Epson printer status check failed: {ex.Message}");
                        return (false, $"Unable to read printer status: {ex.Message}");
                    }
                    finally
                    {
                        try { ensControl.Release(); } catch { }
                    }


                    // Print Epson
                    AppLogger.Log($"Printing resized file (Epson): {resizedPath}");
                    printResult = EpsonPrinterPrint.PrintImage(_printerName, "", resizedPath);

                    AppLogger.Log($"Print Result: {printResult}");
                    AppLogger.Log($"Print Result OrderId: {order.OrderId}");

                    if (printResult != "OK")
                    {
                        AppLogger.Log($"Driver Print Failed: {printResult}");
                        return (false, printResult);
                    }

                    // WAIT FOR REAL HARDWARE RESULT
                    var (success, error) = await WaitForPrintCompletionAsync(_printerName);
                    finalSuccess = success;
                    finalError = error;
                }
                else // Seaory
                {
                    string warning = SeaoryPrinterPrint.CheckPrinterWarning(_printerName);

                    if (warning != "No Warnings")
                        AppLogger.Log($"Printer Warning: {warning}");

                    // Check printer status
                    string status = SeaoryPrinterPrint.CheckPrinterStatus(_printerName);
                    if (status != "Ready")
                    {
                        string parsedMessage = GetMessageFromErrorCodes(status, warning) ?? "";

                        AppLogger.Log($"Printer not ready: {status}");
                        AppLogger.Log($"Parsed Error Message: {parsedMessage}");
                        return (false, parsedMessage);
                    }

                    // Print
                    AppLogger.Log($"Printing resized file: {resizedPath}");
                    printResult = SeaoryPrinterPrint.PrintImage(_printerName, resizedPath);

                    AppLogger.Log($"Print Result: {printResult}");
                    AppLogger.Log($"Print Result OrderId: {order.OrderId}");

                    if (printResult != "OK")
                    {
                        AppLogger.Log($"Driver Print Failed: {printResult}");
                        return (false, printResult);
                    }

                    // WAIT FOR REAL HARDWARE RESULT
                    var (success, error) = await WaitForPrintCompletionAsync(_printerName);
                    finalSuccess = success;
                    finalError = error;
                }

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
        private async Task UpdateOrderStatus(Int64 orderId, int status, string paymentIntentId, string message = "", Int64? orderPaymentTypeId = null)
        {
            var url = $"{AppConfig.ApiBaseUrl}api/update/orderstatus?orderId={orderId}&status={status}&paymentIntentId={paymentIntentId}&message={message}&orderPaymentTypeId={orderPaymentTypeId}";
            try
            {
                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    AppLogger.Log($"UpdateOrderStatus non-success ({(int)resp.StatusCode}) for Order {orderId}; queuing for durable retry.");
                    QueueStatusUpdate(orderId, status, paymentIntentId, message, orderPaymentTypeId, isFree: false);
                }
            }
            catch (Exception ex)
            {
                // Backend down / network lost → persist and retry later so the order stops
                // being reported as "pending" once connectivity returns (T-5.3).
                AppLogger.Log($"Failed to update status for Order {orderId}: {ex.Message}; queuing for durable retry.");
                QueueStatusUpdate(orderId, status, paymentIntentId, message, orderPaymentTypeId, isFree: false);
            }
        }

        // Update Order Status via API For Free Orders
        private async Task UpdateOrderStatusFree(Int64 orderId, int status, string message = "", Int64? orderPaymentTypeId = null)
        {
            var url = $"{AppConfig.ApiBaseUrl}api/update/orderstatus/free?orderId={orderId}&status={status}&message={message}&orderPaymentTypeId={orderPaymentTypeId}";
            try
            {
                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    AppLogger.Log($"UpdateOrderStatusFree non-success ({(int)resp.StatusCode}) for Order {orderId}; queuing for durable retry.");
                    QueueStatusUpdate(orderId, status, null, message, orderPaymentTypeId, isFree: true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to update FREE ORDER {orderId}: {ex.Message}; queuing for durable retry.");
                QueueStatusUpdate(orderId, status, null, message, orderPaymentTypeId, isFree: true);
            }
        }

        // Enqueue a failed status update for durable, persistent retry (T-5.3).
        private static void QueueStatusUpdate(long orderId, int status, string? paymentIntentId, string? message, long? orderPaymentTypeId, bool isFree)
        {
            PendingStatusUpdateStore.Add(new PendingStatusUpdate
            {
                OrderId = orderId,
                Status = status,
                PaymentIntentId = paymentIntentId,
                Message = message,
                OrderPaymentTypeId = orderPaymentTypeId,
                IsFree = isFree
            });
        }

        // Retry any queued status updates; remove each one the backend acknowledges.
        // Called on startup (reconcile, T-5.4) and once per queue cycle (T-5.3).
        private async Task FlushPendingStatusUpdatesAsync()
        {
            if (!PendingStatusUpdateStore.HasAny) return;

            foreach (var u in PendingStatusUpdateStore.Snapshot())
            {
                if (_cts.Token.IsCancellationRequested) return;

                try
                {
                    string url = u.IsFree
                        ? $"{AppConfig.ApiBaseUrl}api/update/orderstatus/free?orderId={u.OrderId}&status={u.Status}&message={u.Message}&orderPaymentTypeId={u.OrderPaymentTypeId}"
                        : $"{AppConfig.ApiBaseUrl}api/update/orderstatus?orderId={u.OrderId}&status={u.Status}&paymentIntentId={u.PaymentIntentId}&message={u.Message}&orderPaymentTypeId={u.OrderPaymentTypeId}";

                    var resp = await _client.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        PendingStatusUpdateStore.Remove(u);
                        AppLogger.Log($"Pending status update delivered for Order {u.OrderId} (status {u.Status}).");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Flush pending status update failed for Order {u.OrderId}: {ex.Message}");
                    // keep it queued for the next attempt
                }
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
                    _printerStatus = 112;
                    finalMessage = "Printer is in Use.";
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

        public string GetEpsonMessageFromErrorCodes(StatusCode sc, ErrorCode ec, WARNING_INFO wc)
        {
            string finalMessage = "";
            switch (sc)
            {
                case StatusCode.ST_Idle:
                    _printerStatus = 1; // Ready
                    finalMessage = "Your printer is online and ready for printing.";
                    break;
                case StatusCode.ST_Busy:
                case StatusCode.ST_Wait:
                case StatusCode.ST_TestPrint:
                    _printerStatus = 40; // System busy
                    finalMessage = "Printer is busy.";
                    break;
                case StatusCode.ST_Cleaning:
                    _printerStatus = 40; // System busy
                    finalMessage = "Printer is cleaning.";
                    break;
                case StatusCode.ST_Error:
                    switch (ec)
                    {
                        case ErrorCode.ERR_CoverOpen:
                            _printerStatus = 14; // Cover open
                            finalMessage = "Cover open. Please close the cover.";
                            break;
                        case ErrorCode.ERR_PaperJam:
                            _printerStatus = 2; // Jam
                            finalMessage = "Paper jam error. Please remove jammed paper.";
                            break;
                        case ErrorCode.ERR_Paperout:
                            _printerStatus = 31; // Paper out
                            finalMessage = "Paper out. No paper detected.";
                            break;
                        case ErrorCode.ERR_Inkout:
                            _printerStatus = 23; // Ink out
                            finalMessage = "Ink cartridge error. Please replace ink.";
                            break;
                        default:
                            _printerStatus = 3; // Other error
                            finalMessage = "Other error. Please check printer.";
                            break;
                    }
                    break;
                default:
                    _printerStatus = 3; // Unknown/Other
                    finalMessage = "Printer is not in a ready state.";
                    break;
            }

            if (wc.InkLowWarning)
            {
                finalMessage += "\n\nAdditional Info: Ink is low.";
            }
            if (wc.OtherWarning)
            {
                finalMessage += "\n\nAdditional Info: Other warning.";
            }

            return finalMessage.Trim();
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
                if (_printerBrand == "Epson")
                {
                    const int MAX_WAIT = 60; // seconds
                    bool printStarted = false;
                    ENS2Control ensControl = ENS2Control.GetInstance();

                    for (int i = 0; i < MAX_WAIT; i++)
                    {
                        await Task.Delay(1000);

                        StatusCode sc;
                        try
                        {
                            ensControl.Initialize(printerName, "");
                            sc = ensControl.GetStatusInformation();

                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"[EPSON-MONITOR] Status query exception: {ex.Message}");
                            continue;
                        }
                        finally
                        {
                            try { ensControl.Release(); } catch { }
                        }

                        // AppLogger.Log($"[EPSON-MONITOR] StatusCode={sc}");



                        if (sc == StatusCode.ST_Busy || sc == StatusCode.ST_Wait || sc == StatusCode.ST_Cleaning)
                        {
                            printStarted = true;
                            continue;
                        }

                        if (sc == StatusCode.ST_Idle)
                        {
                            if (printStarted || i > 5)
                            {
                                return (true, "Print completed successfully");
                            }
                        }

                        if (sc == StatusCode.ST_Error)
                        {
                            ErrorCode ec = ErrorCode.ERR_FatalError;
                            try
                            {
                                ensControl.Initialize(printerName, "");
                                ec = ensControl.GetErrorInformation();

                            }
                            catch { }
                            finally
                            {
                                try { ensControl.Release(); } catch { }
                            }

                            string err = GetEpsonMessageFromErrorCodes(sc, ec, new WARNING_INFO());
                            return (false, err);
                        }
                    }

                    return (true, "Print completed successfully (timeout waiting for state change)");
                }
                else // Seaory
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

                PrintProgressView? printProgressView = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    printProgressView = new PrintProgressView();
                    NavigationService.Navigate(printProgressView);
                });

                var (isSuccess, message) = await PrintOrderAsync(order);

                if (isSuccess)
                {
                    // Record the successful hardware print before any status update (T-5.1),
                    // so a restart can never reprint this order.
                    ProcessedOrderLedger.MarkPrinted(order.OrderId);

                    if (printProgressView != null)
                    {
                        try
                        {
                            await printProgressView.WaitForCompletionAsync();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"PrintProgress wait failed: {ex}");
                        }
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

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        NavigationService.Navigate(new PrintSuccessView());
                    });

                    await Task.Delay(5000);

                    NavigateBackToCapture();

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

        // Navigate Back to Capture View
        private void NavigateBackToCapture()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var currentPage = NavigationService.GetCurrentPage();
                    if (currentPage is Capture)
                        return;

                    Helpers.NavigationService.Navigate(new Capture());
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[STUCK-DEBUG] NavigateBackToCapture ERROR: {ex}");
                }
            });
            AppLogger.Log("[STUCK-DEBUG] NavigateBackToCapture — Dispatcher.Invoke completed");
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

                var response = await _client.PostAsync($"{AppConfig.ApiBaseUrl}api/refund/track-print-failure", content);

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

