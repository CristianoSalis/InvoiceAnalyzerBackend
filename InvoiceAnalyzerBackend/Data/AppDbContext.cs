using InvoiceAnalyzerBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace InvoiceAnalyzerBackend.Data
{
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