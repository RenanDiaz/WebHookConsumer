namespace WebHookConsumer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Configure HttpClientFactory with ProducerApi 
            builder.Services.AddHttpClient("ProducerApi", client =>
            {
                // Get the Producer base URL from configuration
                var producerUrl = builder.Configuration["ProducerApi:BaseUrl"]
                    ?? "http://localhost:5000"; // Default if not specified

                client.BaseAddress = new Uri(producerUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                // You can add more default headers here if needed
                // client.DefaultRequestHeaders.Add("X-API-Key", builder.Configuration["ProducerApi:ApiKey"]);
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

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
