using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using kuboPix.Helpers;
using kuboPix.ViewModels;
using kuboPix.Views;

namespace kuboPix.Services
{
    public class PaymentSessionService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private HubConnection? _hubConnection;
        private IHubProxy? _hubProxy;
        private bool _disposed = false;

        public event Action? ConnectionReconnected;
        public event Action? ConnectionClosed;

        public PaymentSessionService()
        {
            _httpClient = new HttpClient();
        }

        private static string GetCleanBaseUrl()
        {
            return AppConfig.ApiBaseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Step 2 — Start the payment session
        /// POST api/miniapp/payment/session
        /// </summary>
        public async Task<PaymentSessionApiResponse<PaymentSessionResponseData>> CreateSessionAsync(
            string token, long logoWatermarkId, long frameWatermarkId)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{GetCleanBaseUrl()}/api/miniapp/payment/session");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var reqBody = new PaymentSessionRequest
                {
                    LogoWatermarkId = logoWatermarkId,
                    FrameWatermarkId = frameWatermarkId
                };

                string jsonContent = JsonConvert.SerializeObject(reqBody);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<PaymentSessionApiResponse<PaymentSessionResponseData>>(responseJson);
                return result ?? new PaymentSessionApiResponse<PaymentSessionResponseData>
                {
                    status = 0,
                    message = "Invalid response from payment server."
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CreateSessionAsync Exception: {ex.Message}");
                return new PaymentSessionApiResponse<PaymentSessionResponseData>
                {
                    status = -99,
                    message = "Network error while connecting to payment service."
                };
            }
        }

        /// <summary>
        /// Step 3 — Connect SignalR and listen for kioskPaymentUpdated
        /// </summary>
        public async Task<bool> ConnectSignalRAsync(
            long sessionId,
            string kioskSecret,
            Action<PaymentUpdatePayload> onPaymentUpdate)
        {
            try
            {
                string signalRUrl = $"{GetCleanBaseUrl()}/signalr";
                _hubConnection = new HubConnection(signalRUrl);
                _hubProxy = _hubConnection.CreateHubProxy("notifyHub");

                // Rule: Register the handler for kioskPaymentUpdated FIRST before starting connection
                _hubProxy.On<JObject>("kioskPaymentUpdated", payload =>
                {
                    if (payload == null) return;
                    try
                    {
                        var update = payload.ToObject<PaymentUpdatePayload>();
                        if (update != null)
                        {
                            onPaymentUpdate(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Error deserializing kioskPaymentUpdated: {ex.Message}");
                    }
                });

                _hubConnection.Reconnected += () =>
                {
                    AppLogger.Log("SignalR reconnected to notifyHub.");
                    ConnectionReconnected?.Invoke();
                };

                _hubConnection.Closed += () =>
                {
                    AppLogger.Log("SignalR connection closed.");
                    ConnectionClosed?.Invoke();
                };

                await _hubConnection.Start();

                // Then call JoinKioskPayment(sessionId, kioskSecret)
                bool joined = await _hubProxy.Invoke<bool>("JoinKioskPayment", sessionId, kioskSecret);
                AppLogger.Log($"JoinKioskPayment result: {joined}");
                return joined;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ConnectSignalRAsync Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Step 5 — Fallback polling when SignalR is not connected or after reconnect
        /// GET api/miniapp/payment/session/status?sessionId=...&kioskSecret=...
        /// </summary>
        public async Task<PaymentSessionApiResponse<PaymentUpdatePayload>> GetSessionStatusAsync(
            string token, long sessionId, string kioskSecret)
        {
            try
            {
                string url = $"{GetCleanBaseUrl()}/api/miniapp/payment/session/status?sessionId={sessionId}&kioskSecret={Uri.EscapeDataString(kioskSecret)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<PaymentSessionApiResponse<PaymentUpdatePayload>>(responseJson);
                return result ?? new PaymentSessionApiResponse<PaymentUpdatePayload>
                {
                    status = 0,
                    message = "Invalid status response from payment server."
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetSessionStatusAsync Exception: {ex.Message}");
                return new PaymentSessionApiResponse<PaymentUpdatePayload>
                {
                    status = -99,
                    message = "Network error while checking payment status."
                };
            }
        }

        /// <summary>
        /// Step 6 — Cancel session on Back button or countdown reaching zero
        /// POST api/miniapp/payment/session/cancel
        /// </summary>
        public async Task<PaymentSessionApiResponse<PaymentUpdatePayload>> CancelSessionAsync(
            string token, long sessionId, string kioskSecret)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{GetCleanBaseUrl()}/api/miniapp/payment/session/cancel");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var cancelReq = new PaymentSessionCancelRequest
                {
                    SessionId = sessionId,
                    KioskSecret = kioskSecret
                };

                string jsonContent = JsonConvert.SerializeObject(cancelReq);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<PaymentSessionApiResponse<PaymentUpdatePayload>>(responseJson);
                return result ?? new PaymentSessionApiResponse<PaymentUpdatePayload>
                {
                    status = 0,
                    message = "Failed to cancel payment session."
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CancelSessionAsync Exception: {ex.Message}");
                return new PaymentSessionApiResponse<PaymentUpdatePayload>
                {
                    status = -99,
                    message = "Network error while canceling payment session."
                };
            }
        }

        /// <summary>
        /// Step 7 — Execute dynamic CreateOrder payload from server
        /// Generic dispatch without hard-coding payment methods
        /// </summary>
        public async Task<long> ExecuteCreateOrderAsync(string token, CreateOrderInstruction instruction)
        {
            if (instruction == null || string.IsNullOrWhiteSpace(instruction.Url))
            {
                AppLogger.Log("ExecuteCreateOrderAsync: CreateOrder instruction is missing or URL is empty.");
                return 0;
            }

            try
            {
                string targetUrl = instruction.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? instruction.Url
                    : $"{GetCleanBaseUrl()}/{instruction.Url.TrimStart('/')}";

                var httpMethod = new HttpMethod(string.IsNullOrWhiteSpace(instruction.Method) ? "POST" : instruction.Method.ToUpperInvariant());
                using var request = new HttpRequestMessage(httpMethod, targetUrl);

                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                string bodyJson = "{}";
                if (instruction.Body != null)
                {
                    bodyJson = instruction.Body.ToString(Formatting.None);
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                }

                AppLogger.Log($"ExecuteCreateOrderAsync Request: {httpMethod} {targetUrl} | Body: {bodyJson}");

                var response = await _httpClient.SendAsync(request);
                string responseJson = await response.Content.ReadAsStringAsync();

                AppLogger.Log($"ExecuteCreateOrderAsync Response ({(int)response.StatusCode}): {responseJson}");

                var resToken = JToken.Parse(responseJson);

                // Extract orderId using OrderIdField (e.g., "data.orderId" or "orderId")
                long orderId = 0;
                if (!string.IsNullOrWhiteSpace(instruction.OrderIdField))
                {
                    var selected = resToken.SelectToken(instruction.OrderIdField);
                    if (selected != null && long.TryParse(selected.ToString(), out long parsedId))
                    {
                        orderId = parsedId;
                    }
                }

                // Fallback checks if orderId was not resolved via OrderIdField
                if (orderId <= 0)
                {
                    if (resToken["orderId"] != null && long.TryParse(resToken["orderId"]!.ToString(), out long id1))
                    {
                        orderId = id1;
                    }
                    else if (resToken["data"]?["orderId"] != null && long.TryParse(resToken["data"]!["orderId"]!.ToString(), out long id2))
                    {
                        orderId = id2;
                    }
                }

                AppLogger.Log($"ExecuteCreateOrderAsync: Resulting OrderId = {orderId}");
                return orderId;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ExecuteCreateOrderAsync Exception: {ex.Message}");
                return 0;
            }
        }

        public void DisconnectSignalR()
        {
            try
            {
                _hubConnection?.Stop();
                _hubConnection?.Dispose();
                _hubConnection = null;
                _hubProxy = null;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"DisconnectSignalR Exception: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DisconnectSignalR();
            _httpClient?.Dispose();
        }
    }
}
