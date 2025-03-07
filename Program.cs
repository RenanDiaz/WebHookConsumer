using Consumer.Controllers;

namespace WebHookConsumer
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register the webhook secret store as a singleton
            builder.Services.AddSingleton<IWebhookSecretStore, InMemoryWebhookSecretStore>();
            builder.Services.AddSingleton<IConsumerStatusStore, InMemoryConsumerStatusStore>();

            // Configure HttpClientFactory with ProducerApi and SSL bypass for development
            builder.Services.AddHttpClient("ProducerApi", client =>
            {
                // Get the Producer base URL from configuration
                var producerUrl = builder.Configuration["ProducerApi:BaseUrl"]
                    ?? "http://localhost:5000"; // Default if not specified

                client.BaseAddress = new Uri(producerUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                // Only bypass SSL validation in Development environment
                if (builder.Environment.IsDevelopment())
                {
                    return new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                    };
                }

                // Use default validation in production
                return new HttpClientHandler();
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Comment out HttpsRedirection for local development if needed
            // app.UseHttpsRedirection();

            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}