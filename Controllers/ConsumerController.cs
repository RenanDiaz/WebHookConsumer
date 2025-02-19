using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace WebHookConsumer.Controllers
{
    public class SvixPayload
    {
        public int numberId { get; set; }
    }

    [ApiController]
    [Route("[controller]")]
    public class ConsumerController : ControllerBase
    {
        private const string WebhookSecret = "whsec_C2FVsBQIhrscChlQIMV+b5sSYspob7oD";

        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook()
        {
            // Leer el cuerpo de la solicitud
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // Verificar la firma del webhook
            if (!VerifyWebhookSignature(Request.Headers, body))
            {
                return BadRequest("Invalid signature");
            }

            // En tu método de controlador
            var payload = JsonSerializer.Deserialize<SvixPayload>(body);
            if (payload != null)
            {
                Console.WriteLine($"Número recibido: {payload.numberId}");
                return Ok($"Número {payload.numberId} procesado exitosamente");
                }
            else
            {
                return BadRequest("No se pudo deserializar el payload");
            }

        }

        private bool VerifyWebhookSignature(IHeaderDictionary headers, string body)
        {
            if (!headers.TryGetValue("svix-signature", out var signatureHeader) ||
                !headers.TryGetValue("svix-timestamp", out var timestampHeader) ||
                !headers.TryGetValue("svix-Id", out var svixId))
            {
                return false;
            }

            var timestamp = timestampHeader.ToString();
            var signatureValue = signatureHeader.ToString().Trim('{', '}');
            var signatureParts = signatureValue.Split(',');

            if (signatureParts.Length != 2 || !signatureParts[0].StartsWith("v1"))
            {
                return false;
            }

            var providedSignature = signatureParts[1];

            string signedContent = $"{svixId}.{timestamp}.{body}";
            string secret = WebhookSecret;

            //// Extraer la parte base64 del secreto (después del '_')
            string secretBase64 = secret.Split('_')[1];
            byte[] secretBytes = Convert.FromBase64String(secretBase64);

            using (var hmac = new HMACSHA256(secretBytes))
            {
                byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
                return providedSignature == Convert.ToBase64String(signatureBytes);
            }
        }
    }
}
