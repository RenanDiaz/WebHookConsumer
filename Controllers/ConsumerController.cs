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

        public ConsumerController(
            ILogger<ConsumerController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("ProducerApi");
            _configuration = configuration;
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(SubscriptionSettings settings)
        {
            try
            {
                // Determine the consumer's webhook URL
                var baseUrl = _configuration["ConsumerApp:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var webhookEndpoint = $"{baseUrl}/api/consumer/receive-webhook";

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

                return Ok(new
                {
                    Success = true,
                    EndpointId = result?.EndpointId,
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
                // Send unsubscription request to the producer
                var response = await _httpClient.DeleteAsync(
                    $"api/producer/unsubscribe?endpointId={endpointId}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new { Success = false, Message = $"Failed to unsubscribe: {errorContent}" });
                }

                return Ok(new { Success = true, Message = "Successfully unsubscribed from webhooks" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from webhooks");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("receive-webhook")]
        public async Task<IActionResult> ReceiveWebhook([FromBody] JsonElement body)
        {
            try
            {
                var secret = _configuration["Webhook:Secret"];

                if (string.IsNullOrEmpty(secret))
                {
                    return BadRequest(new { Success = false, Message = "Webhook secret not configured" });
                }

                // Get the Svix headers
                var svixHeaders = Request.Headers
                    .Where(h => h.Key.StartsWith("svix-", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString());

                var payload = JsonSerializer.Serialize(body);
                _logger.LogInformation("Received webhook payload: {Payload}", payload);

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

                // Process the webhook
                var webhookData = JsonSerializer.Deserialize<WebhookPayload>(payload);

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
                case "user.created":
                    await HandleUserCreatedAsync(webhookData.Payload);
                    break;
                case "order.completed":
                    await HandleOrderCompletedAsync(webhookData.Payload);
                    break;
                // Add more event handlers as needed
                default:
                    _logger.LogInformation("Unhandled event type: {eventType}", webhookData.EventType);
                    break;
            }
        }

        private Task HandleUserCreatedAsync(JsonElement payload)
        {
            // Implement user creation handling
            _logger.LogInformation("Processing user.created event");
            return Task.CompletedTask;
        }

        private Task HandleOrderCompletedAsync(JsonElement payload)
        {
            // Implement order completion handling
            _logger.LogInformation("Processing order.completed event");
            return Task.CompletedTask;
        }
    }

    public class SubscriptionSettings
    {
        public string ConsumerId { get; set; }
    }

    public class SubscriptionResult
    {
        public bool Success { get; set; }
        public string EndpointId { get; set; }
        public string Secret { get; set; }
        public string Message { get; set; }
    }

    public class WebhookPayload
    {
        public string EventType { get; set; }
        public JsonElement Payload { get; set; }
    }
}