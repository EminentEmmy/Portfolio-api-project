using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using PortfolioApi.Data;
using PortfolioApi.Exceptions;
using PortfolioApi.Services;

using Prometheus;

namespace PortfolioApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // The container services.
            builder.Services.AddControllers()
                .AddJsonOptions(o =>
                    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddScoped<ITransactionService, TransactionService>();
            builder.Services.AddScoped<IAssetService, AssetService>();
            builder.Services.AddScoped<IPortfolioService, PortfolioService>();

            // Global error handling (RFC 7807 ProblemDetails).
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
            builder.Services.AddHealthChecks();

            var app = builder.Build();

            app.UseHttpMetrics();

            app.UseExceptionHandler();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();
            app.MapMetrics("/metrics");
            app.MapHealthChecks("/health");

            app.MapControllers();

            app.Run();
        }
    }

    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }
}