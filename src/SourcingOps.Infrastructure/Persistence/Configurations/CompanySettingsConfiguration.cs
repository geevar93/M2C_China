using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Infrastructure.Persistence.Configurations;

/// <summary>
/// TECH_SPEC §6's single-row invoice billing-block table. Shape only this pass — see
/// <see cref="CompanySettings"/>'s doc comment for why no row is seeded.
/// </summary>
public class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> b)
    {
        // The CHECK constraint (not just "HasKey", which only prevents duplicate ids) is
        // what actually enforces single-row: it makes every possible id except the one
        // singleton value invalid, so a second row can never be inserted regardless of what
        // id the inserting code picks. See feedback-efcore-npgsql-gotchas memory:
        // HasCheckConstraint must be configured inside ToTable(), not as a separate call.
        b.ToTable("company_settings", t => t.HasCheckConstraint(
            "ck_company_settings_singleton",
            $"id = '{CompanySettings.SingletonId}'"));

        b.HasKey(x => x.Id);

        b.Property(x => x.LegalEntityName).HasMaxLength(300);
        b.Property(x => x.Gstin).HasMaxLength(20);
        b.Property(x => x.RegisteredAddress).HasMaxLength(1000);
        b.Property(x => x.BankAccountName).HasMaxLength(300);
        b.Property(x => x.BankAccountNumber).HasMaxLength(50);
        b.Property(x => x.BankIfsc).HasMaxLength(20);
        b.Property(x => x.BankBranch).HasMaxLength(200);
        b.Property(x => x.InvoiceNumberPrefix).HasMaxLength(20);
        b.Property(x => x.LogoFilePath).HasMaxLength(1000);

        b.HasOne(x => x.UpdatedBy).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
