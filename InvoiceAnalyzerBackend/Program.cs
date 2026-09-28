using InvoiceAnalyzerBackend.Data;
using InvoiceAnalyzerBackend.HostedServices;
using InvoiceAnalyzerBackend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;


///<summary>    
///punto di ingresso dell'applicazione ASP.NET Core. Configura i servizi, il database, i middleware e avvia il server.
/// </summary>
var builder = WebApplication.CreateBuilder(args);

// DbContext: SQLite file in Data/invoices.db
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDir);
var sqliteConn = $"Data Source={Path.Combine(dataDir, "invoices.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConn));

builder.Services.AddControllers();

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
///<summary>
///Crea uno scope temporaneo per accedere ai servizi
///Recupera AppDbContext
///Chiama EnsureCreated(): crea il database e tutte le tabelle se non esistono
///Risultato: La tabella Invoices e InvoiceJobs sono pronte al primo avvio.
///</summary>
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