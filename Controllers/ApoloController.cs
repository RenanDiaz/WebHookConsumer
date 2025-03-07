using Microsoft.AspNetCore.Mvc;

namespace Consumer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApoloController : ConsumersController
    {
        private string _consumerName = "apolo";

        public ApoloController(ILogger<ApoloController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, IWebhookSecretStore secretStore = null, IConsumerStatusStore statusStore = null) : base(logger, httpClientFactory, configuration, secretStore, statusStore)
        {
        }

        [HttpPost("transactions")]
        public async Task<IActionResult> Transactions()
        {
            var consumerStatus = await _statusStore.IsConsumerActiveAsync(_consumerName);
            if (consumerStatus == false)
            {
                return NotFound(new { Success = false, Message = "Consumer is not active" });
            }
            return await ProcessTransactionWebhook();
        }

        [HttpPost("domain")]
        public async Task<IActionResult> Domain()
        {
            var consumerStatus = await _statusStore.IsConsumerActiveAsync(_consumerName);
            if (consumerStatus == false)
            {
                return NotFound(new { Success = false, Message = "Consumer is not active" });
            }
            return await ProcessDomainWebhook();
        }
    }
}
