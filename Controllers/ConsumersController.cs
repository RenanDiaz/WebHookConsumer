using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Svix;

namespace Consumer.Controllers
{
    public class ConsumersController : ControllerBase
    {
        private readonly ILogger<ConsumersController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IWebhookSecretStore _secretStore;

        public ConsumersController(
            ILogger<ConsumersController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebhookSecretStore secretStore = null)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("ProducerApi");
            _configuration = configuration;
            _secretStore = secretStore ?? new InMemoryWebhookSecretStore();
        }

        public async Task<IActionResult> ProcessTransactionWebhook()
        {
            try
            {
                var payload = await new StreamReader(Request.Body).ReadToEndAsync();
                Console.WriteLine($"Received webhook payload: {payload}");
                var webhookData = JsonSerializer.Deserialize<TransactionPayload>(payload);
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

        public async Task<IActionResult> ProcessDomainWebhook()
        {
            try
            {
                var payload = await new StreamReader(Request.Body).ReadToEndAsync();
                Console.WriteLine($"Received webhook payload: {payload}");
                var webhookData = JsonSerializer.Deserialize<DomainPayload>(payload);
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

        private async Task ProcessWebhookAsync(WebhookPayload webhookData)
        {
            // Implement your webhook processing logic here
            // This is where you would handle different event types
            if (webhookData == null) return;

            switch (webhookData.EventType)
            {
                case "transaction.deposit":
                    await HandleDeposit(webhookData);
                    break;
                case "transaction.withdrawal":
                    await HandleWithdrawal(webhookData);
                    break;
                case "transaction.authorization":
                    await HandleAuthorization(webhookData);
                    break;
                case "transaction.refund":
                    await HandleRefund(webhookData);
                    break;
                // Add more event handlers as needed
                default:
                    Console.WriteLine($"Unhandled event type: {webhookData.EventType}");
                    break;
            }
        }

        private Task HandleDeposit(WebhookPayload payload)
        {
            // Implement deposit handling
            Console.WriteLine("Processing transaction.deposit event");
            return Task.CompletedTask;
        }

        private Task HandleWithdrawal(WebhookPayload payload)
        {
            // Implement withdrawal handling
            Console.WriteLine("Processing transaction.withdrawal event");
            return Task.CompletedTask;
        }

        private Task HandleAuthorization(WebhookPayload payload)
        {
            // Implement authorization handling
            Console.WriteLine("Processing transaction.authorization event");
            return Task.CompletedTask;
        }

        private Task HandleRefund(WebhookPayload payload)
        {
            // Implement refund handling
            Console.WriteLine("Processing transaction.refund event");
            return Task.CompletedTask;
        }

        private Task HandleDomainChange(WebhookPayload payload)
        {
            // Implement domain change handling
            Console.WriteLine("Processing domain.change event");
            return Task.CompletedTask;
        }
    }

    public class WebhookPayload
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }
    }

    public class TransactionPayload : WebhookPayload
    {
        [JsonPropertyName("customerId")]
        public string CustomerId { get; set; }
        [JsonPropertyName("transactionId")]
        public string TransactionId { get; set; }
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; }
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }
        [JsonPropertyName("amountFormatted")]
        public string AmountFormatted { get; set; }
        [JsonPropertyName("currencyCode")]
        public string CurrencyCode { get; set; }
        [JsonPropertyName("unicode")]
        public string Unicode { get; set; }
    }

    public class DomainPayload : WebhookPayload
    {
        [JsonPropertyName("domainId")]
        public string DomainId { get; set; }
        [JsonPropertyName("domainName")]
        public string DomainName { get; set; }
    }
}