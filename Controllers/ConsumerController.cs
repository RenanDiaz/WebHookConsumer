using Microsoft.AspNetCore.Mvc;
using Svix;
using Svix.Api;
using Svix.Client;
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
                // Generate a webhook secret
                var secret = Guid.NewGuid().ToString("N");

                // Save the secret in your application for later verification
                // In a production app, you would store this securely
                // For demo purposes, we'll just log it
                _logger.LogInformation($"Generated webhook secret: {secret}");

                // Determine the consumer's webhook URL
                var baseUrl = _configuration["ConsumerApp:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var webhookEndpoint = $"{baseUrl}/api/consumer/receive-webhook";

                // Create subscription request
                var subscriptionRequest = new
                {
                    ConsumerId = settings.ConsumerId ?? "default-consumer",
                    WebhookUrl = webhookEndpoint,
                    Secret = secret
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
                    Secret = secret,
                    Message = "Successfully subscribed to webhooks"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to webhooks");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("receive-webhook")]
        public async Task<IActionResult> ReceiveWebhook()
        {
            try
            {
                // Get the webhook secret from configuration or database
                var secret = _configuration["Webhook:Secret"];

                if (string.IsNullOrEmpty(secret))
                {
                    _logger.LogWarning("No webhook secret configured!");
                    return BadRequest(new { Success = false, Message = "Webhook secret not configured" });
                }

                // Get the webhook headers
                var svixId = Request.Headers["svix-id"].ToString();
                var svixTimestamp = Request.Headers["svix-timestamp"].ToString();
                var svixSignature = Request.Headers["svix-signature"].ToString();

                if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTimestamp) || string.IsNullOrEmpty(svixSignature))
                {
                    _logger.LogWarning("Missing Svix webhook headers");
                    return BadRequest(new { Success = false, Message = "Missing webhook headers" });
                }

                // Read the request body
                Request.EnableBuffering();
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                Request.Body.Position = 0;

                try
                {
                    // Verify the webhook signature
                    var wh = new Webhook(secret);
                    wh.Verify(body, svixId, svixTimestamp, svixSignature);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook signature verification failed");
                    return BadRequest(new { Success = false, Message = "Invalid webhook signature" });
                }

                // Process the webhook
                var webhookData = JsonSerializer.Deserialize<WebhookPayload>(body);

                // Log the received webhook
                _logger.LogInformation($"Received webhook: {webhookData?.EventType}");

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
                    _logger.LogInformation($"Unhandled event type: {webhookData.EventType}");
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
        public string Message { get; set; }
    }

    public class WebhookPayload
    {
        public string EventType { get; set; }
        public JsonElement Payload { get; set; }
    }
}