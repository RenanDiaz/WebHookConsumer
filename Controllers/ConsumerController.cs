using Microsoft.AspNetCore.Mvc;
using Svix;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Consumer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConsumerController : ControllerBase
    {
        private readonly ILogger<ConsumerController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IWebhookSecretStore _secretStore;

        public ConsumerController(
            ILogger<ConsumerController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebhookSecretStore secretStore = null)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("ProducerApi");
            _configuration = configuration;
            _secretStore = secretStore ?? new InMemoryWebhookSecretStore();
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(SubscriptionSettings settings)
        {
            try
            {
                // Determine the consumer's webhook URL
                var baseUrl = _configuration["ConsumerApp:BaseUrl"];
                var webhookEndpoint = $"{baseUrl}{settings.CallbackPath}";

                // Create subscription request - let Svix generate the secret
                var subscriptionRequest = new
                {
                    ConsumerId = settings.ConsumerId ?? "default-consumer",
                    WebhookUrl = webhookEndpoint,
                    Secret = "" // Empty string - Svix will generate a valid secret
                };

                // Send subscription request to the producer
                var response = await _httpClient.PostAsJsonAsync(
                    "api/producer/subscribe",
                    subscriptionRequest);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new { Success = false, Message = $"Failed to subscribe: {errorContent}" });
                }

                var result = await response.Content.ReadFromJsonAsync<SubscriptionResult>();

                if (result == null || string.IsNullOrEmpty(result.EndpointId))
                {
                    return BadRequest(new { Success = false, Message = "Failed to subscribe to webhooks" });
                }

                var secretResponse = await _httpClient.GetAsync($"api/producer/secret?endpointId={result.EndpointId}");
                var secretResult = await secretResponse.Content.ReadFromJsonAsync<SecretResult>();

                if (secretResult == null || string.IsNullOrEmpty(secretResult.Secret))
                {
                    return BadRequest(new { Success = false, Message = "Failed to retrieve webhook secret" });
                }

                // Store the secret for later webhook verification
                await _secretStore.StoreSecretAsync(webhookEndpoint, secretResult.Secret);
                Console.WriteLine($"Stored webhook secret for endpoint {webhookEndpoint}");

                return Ok(new
                {
                    Success = true,
                    result.EndpointId,
                    WebhookUrl = webhookEndpoint,
                    Message = "Successfully subscribed to webhooks"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to webhooks");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpDelete("unsubscribe")]
        public async Task<IActionResult> Unsubscribe(string endpointId)
        {
            try
            {
                var endpointResponse = await _httpClient.GetAsync($"api/producer/endpoint/{endpointId}");
                if (!endpointResponse.IsSuccessStatusCode)
                {
                    var errorContent = await endpointResponse.Content.ReadAsStringAsync();
                    return BadRequest(new { Success = false, Message = $"Failed to retrieve endpoint: {errorContent}" });
                }

                var endpointResult = await endpointResponse.Content.ReadFromJsonAsync<Endpoint>();
                if (endpointResult == null)
                {
                    return BadRequest(new { Success = false, Message = "Failed to retrieve endpoint" });
                }

                // Send unsubscription request to the producer
                var response = await _httpClient.DeleteAsync(
                    $"api/producer/unsubscribe/{endpointId}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new { Success = false, Message = $"Failed to unsubscribe: {errorContent}" });
                }

                // Remove the secret from storage
                await _secretStore.StoreSecretAsync(endpointResult.Url, null);
                _logger.LogInformation($"Removed webhook secret for endpoint {endpointResult.Url}");

                return Ok(new { Success = true, Message = "Successfully unsubscribed from webhooks" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from webhooks");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("receive-webhook")]
        public async Task<IActionResult> ReceiveWebhook()
        {
            try
            {
                var payload = await new StreamReader(Request.Body).ReadToEndAsync();
                Console.WriteLine($"Received webhook payload: {payload}");
                var webhookData = JsonSerializer.Deserialize<WebhookPayload>(payload);
                if (webhookData == null)
                {
                    return BadRequest(new { Success = false, Message = "Invalid webhook payload" });
                }
                var requestUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}{HttpContext.Request.Path}";
                var secret = await _secretStore.GetSecretAsync(requestUrl);
                Console.WriteLine($"Retrieved webhook secret {secret} for endpoint {requestUrl}");

                if (string.IsNullOrEmpty(secret))
                {
                    _logger.LogWarning("Webhook secret not configured");
                    return BadRequest(new { Success = false, Message = "Webhook secret not configured" });
                }

                // Get the Svix headers
                var svixHeaders = Request.Headers
                    .Where(h => h.Key.StartsWith("svix-", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString());

                try
                {
                    var webHeaderCollection = new WebHeaderCollection();
                    foreach (var header in svixHeaders)
                    {
                        webHeaderCollection.Add(header.Key, header.Value);
                    }
                    var webhook = new Webhook(secret);
                    webhook.Verify(payload, webHeaderCollection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook signature verification failed");
                    return BadRequest(new { Success = false, Message = "Invalid webhook signature" });
                }

                // Process the webhook based on event type
                await ProcessWebhookAsync(webhookData);

                return Ok(new { Success = true, Message = "Webhook received and processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("order/completed")]
        public async Task<IActionResult> OrderCompleted()
        {
            try
            {
                var payload = await new StreamReader(Request.Body).ReadToEndAsync();
                Console.WriteLine($"Received webhook payload: {payload}");
                var webhookData = JsonSerializer.Deserialize<WebhookPayload>(payload);
                if (webhookData == null)
                {
                    return BadRequest(new { Success = false, Message = "Invalid webhook payload" });
                }
                var requestUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}{HttpContext.Request.Path}";
                var secret = await _secretStore.GetSecretAsync(requestUrl);
                Console.WriteLine($"Retrieved webhook {secret} for endpoint {requestUrl}");

                if (string.IsNullOrEmpty(secret))
                {
                    _logger.LogWarning("Webhook secret not configured");
                    return BadRequest(new { Success = false, Message = "Webhook secret not configured" });
                }

                // Get the Svix headers
                var svixHeaders = Request.Headers
                    .Where(h => h.Key.StartsWith("svix-", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString());

                try
                {
                    var webHeaderCollection = new WebHeaderCollection();
                    foreach (var header in svixHeaders)
                    {
                        webHeaderCollection.Add(header.Key, header.Value);
                    }
                    var webhook = new Webhook(secret);
                    webhook.Verify(payload, webHeaderCollection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook signature verification failed");
                    return BadRequest(new { Success = false, Message = "Invalid webhook signature" });
                }

                // Process the webhook based on event type
                await ProcessWebhookAsync(webhookData);

                return Ok(new { Success = true, Message = "Webhook received and processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("secrets/restore")]
        public async Task<IActionResult> RestoreSecrets()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/producer/endpoints");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new { Success = false, Message = $"Failed to retrieve endpoints: {errorContent}" });
                }

                var data = await response.Content.ReadFromJsonAsync<EndpointsResponse>();

                if (data == null || !data.Success || data.Endpoints == null || data.Endpoints.Count == 0)
                {
                    return BadRequest(new { Success = false, Message = "No endpoints found" });
                }

                foreach (var endpoint in data.Endpoints)
                {
                    var secretResponse = await _httpClient.GetAsync($"api/producer/secret?endpointId={endpoint.Id}");
                    var secretResult = await secretResponse.Content.ReadFromJsonAsync<SecretResult>();

                    if (secretResult == null || string.IsNullOrEmpty(secretResult.Secret))
                    {
                        return BadRequest(new { Success = false, Message = "Failed to retrieve webhook secret" });
                    }

                    // Store the secret for later webhook verification
                    await _secretStore.StoreSecretAsync(endpoint.Url, secretResult.Secret);
                    _logger.LogInformation($"Stored webhook secret for endpoint {endpoint.Url}");
                }

                return Ok(new { Success = true, Message = $"Successfully restored {data.Endpoints.Count} webhook secret(s)" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook secrets");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok("Test successful");
        }

        private async Task ProcessWebhookAsync(WebhookPayload webhookData)
        {
            // Implement your webhook processing logic here
            // This is where you would handle different event types
            if (webhookData == null) return;

            switch (webhookData.EventType)
            {
                case "perform.cmd":
                    await HandlePerformCommand(webhookData);
                    break;
                case "order.completed":
                    await HandleOrderCompletedAsync(webhookData);
                    break;
                // Add more event handlers as needed
                default:
                    Console.WriteLine($"Unhandled event type: {webhookData.EventType}");
                    break;
            }
        }

        private Task HandlePerformCommand(WebhookPayload payload)
        {
            // Implement user creation handling
            Console.WriteLine("Processing user.created event");
            return Task.CompletedTask;
        }

        private Task HandleOrderCompletedAsync(WebhookPayload payload)
        {
            // Implement order completion handling
            Console.WriteLine("Processing order.completed event");
            return Task.CompletedTask;
        }
    }

    public class SubscriptionSettings
    {
        public string ConsumerId { get; set; }
        public string CallbackPath { get; set; }
    }

    public class SubscriptionResult
    {
        public bool Success { get; set; }
        public string EndpointId { get; set; }
        public string Message { get; set; }
    }

    public class SecretResult
    {
        public bool Success { get; set; }
        public string Secret { get; set; }
    }

    public class WebhookPayload
    {
        public string EndpointId { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
    }

    public class EndpointsResponse
    {
        public bool Success { get; set; }
        public List<Endpoint> Endpoints { get; set; }
    }

    public class Endpoint
    {
        public string Id { get; set; }
        public string Url { get; set; }
    }

    // Interface for storing webhook secrets
    public interface IWebhookSecretStore
    {
        Task<string> GetSecretAsync(string endpointUrl);
        Task StoreSecretAsync(string endpointUrl, string secret);
    }

    // Simple in-memory implementation for development
    public class InMemoryWebhookSecretStore : IWebhookSecretStore
    {
        private static readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

        public Task<string> GetSecretAsync(string endpointUrl)
        {
            return Task.FromResult(_secrets.TryGetValue(endpointUrl, out var secret) ? secret : null);
        }

        public Task StoreSecretAsync(string endpointUrl, string secret)
        {
            _secrets[endpointUrl] = secret;
            return Task.CompletedTask;
        }
    }
}