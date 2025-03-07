using Microsoft.AspNetCore.Mvc;

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
        private readonly IConsumerStatusStore _statusStore;

        public ConsumerController(
            ILogger<ConsumerController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebhookSecretStore secretStore = null,
            IConsumerStatusStore statusStore = null)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("ProducerApi");
            _configuration = configuration;
            _secretStore = secretStore ?? new InMemoryWebhookSecretStore();
            _statusStore = statusStore ?? new InMemoryConsumerStatusStore();
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
                    Affiliate = settings.ConsumerName,
                    Channels = settings.TransactionTypes,
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

        [HttpPost("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] StatusPayload payload)
        {
            try
            {
                await _statusStore.SetConsumerActiveAsync(payload.ConsumerName, payload.IsActive);
                return Ok(new { Success = true, Message = "Consumer status updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating consumer status");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

    }

    public enum TransactionTypes
    {
        Public,
        Deposit,
        Withdrawal,
        Authorization,
        Refund,
        Domain
    }

    public class SubscriptionSettings
    {
        public string ConsumerName { get; set; }
        public string CallbackPath { get; set; }
        public List<TransactionTypes> TransactionTypes { get; set; }
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

    public class StatusPayload
    {
        public string ConsumerName { get; set; }
        public bool IsActive { get; set; }
    }

    public interface IWebhookSecretStore
    {
        Task<string> GetSecretAsync(string endpointUrl);
        Task StoreSecretAsync(string endpointUrl, string secret);
    }

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

    public interface IConsumerStatusStore
    {
        Task<bool> IsConsumerActiveAsync(string consumerName);
        Task SetConsumerActiveAsync(string consumerName, bool isActive);
    }

    public class InMemoryConsumerStatusStore : IConsumerStatusStore
    {
        private static readonly Dictionary<string, bool> _status = new Dictionary<string, bool>();

        public Task<bool> IsConsumerActiveAsync(string consumerName)
        {
            return Task.FromResult(_status.TryGetValue(consumerName, out var isActive) && isActive != false);
        }

        public Task SetConsumerActiveAsync(string consumerName, bool isActive)
        {
            _status[consumerName] = isActive;
            return Task.CompletedTask;
        }
    }
}