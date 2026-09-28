using InvoiceAnalyzerBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace InvoiceAnalyzerBackend.Data
{

    /// <summary>
    /// AppDbContext è il context di Entity Framework Core che gestisce la comunicazione tra
    /// l'applicazione e il database. Funge da intermediario (ORM - Object-Relational Mapping) per leggere e scrivere i dati.
    /// Questa classe configura il database: dice a Entity Framework quali tabelle esistono, quali sono le loro colonne,    
    /// come sono collegate, quali indici creare, e quali regole applicare (come le eliminazioni in cascata).
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Invoice> Invoices { get; set; } = null!;
        public DbSet<InvoiceJob> InvoiceJobs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Invoice>(b =>
            {
                b.HasKey(i => i.Id);
                b.HasIndex(i => i.InvoiceNumber);
                b.HasIndex(i => i.VendorName);
                b.Property(i => i.TotalAmount).HasPrecision(18, 2);
                b.Property(i => i.TaxAmount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<InvoiceJob>(b =>
            {
                b.HasKey(j => j.Id);
                b.HasOne(j => j.Invoice)
                 .WithMany()
                 .HasForeignKey(j => j.InvoiceId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}