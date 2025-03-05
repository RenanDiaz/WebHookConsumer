using Microsoft.AspNetCore.Mvc;

namespace Consumer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CronosController : ConsumersController
    {
        public CronosController(ILogger<CronosController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, IWebhookSecretStore secretStore = null) : base(logger, httpClientFactory, configuration, secretStore)
        {
        }

        [HttpPost("transactions")]
        public async Task<IActionResult> Transactions()
        {
            return await ProcessTransactionWebhook();
        }

        [HttpPost("domain")]
        public async Task<IActionResult> Domain()
        {
            return await ProcessDomainWebhook();
        }
    }
}
