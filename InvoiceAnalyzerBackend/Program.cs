using System;
using System.IO;
using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.HostedServices;
using InvoiceAnalyzerBackend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurazione DbContext SQLite
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDir);
var sqliteConn = $"Data Source={Path.Combine(dataDir, "invoices.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConn));

builder.Services.AddControllers();

// 2. Registrazione Opzioni IA
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));

// 3. Registrazione IA Service (HttpAiService o NullAiService)
var aiEndpoint = builder.Configuration["Ai:Endpoint"];

if (!string.IsNullOrEmpty(aiEndpoint))
{
    builder.Services.AddHttpClient<IAiService, HttpAiService>();
}
else
{
    builder.Services.AddSingleton<IAiService, NullAiService>();
}

// 4. Registrazione Servizi applicativi
builder.Services.AddScoped<IOcrService, MockOcrService>();
builder.Services.AddScoped<IAnalyzerService, AnalyzerService>();

// 5. Hosted background processor per i job
builder.Services.AddHostedService<JobProcessorHostedService>();

// 6. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 7. CORS per Client Dev (Vite / React)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 8. Inizializzazione Database all'avvio
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// 9. Configurazione Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.RoutePrefix = "swagger";
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "InvoiceAnalyzerBackend v1");
    });
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthorization();

app.MapControllers();

app.Run();