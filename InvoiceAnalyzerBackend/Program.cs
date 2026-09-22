using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.HostedServices;
using InvoiceAnalyzerBackend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// DbContext: SQLite file in Data/invoices.db
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDir);
var sqliteConn = $"Data Source={Path.Combine(dataDir, "invoices.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConn));

// Add services
builder.Services.AddControllers();

//builder.Services.AddSingleton<InvoiceAnalyzerBackend.Services.IOcrService, InvoiceAnalyzerBackend.Services.MockOcrService>();
//// OCR service (Mock / PdfPig-based)
//builder.Services.AddSingleton<InvoiceAnalyzerBackend.Services.IOcrService, InvoiceAnalyzerBackend.Services.MockOcrService>();

//// Analyzer: rule-based
//builder.Services.AddScoped<InvoiceAnalyzerBackend.Services.IAnalyzerService, InvoiceAnalyzerBackend.Services.AnalyzerService>();

//// Hosted background processor
//builder.Services.AddHostedService<InvoiceAnalyzerBackend.HostedServices.JobProcessorHostedService>();

//builder.Services.AddSingleton<IOcrService, MockOcrService>();

//builder.Services.AddScoped<IAnalyzerService, AnalyzerService>();

//builder.Services.AddHostedService<JobProcessorHostedService>();

// OCR service (Mock / PdfPig-based)
builder.Services.AddSingleton<IOcrService, MockOcrService>();

// Analyzer: rule-based
builder.Services.AddScoped<IAnalyzerService, AnalyzerService>();

// Hosted background processor
builder.Services.AddHostedService<JobProcessorHostedService>();



// Swagger / OpenAPI via Swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS per dev client (Vite default)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Ensure DB and schema are created at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure pipeline
app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "swagger";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "InvoiceAnalyzerBackend v1");
});

app.MapControllers();
app.Run();