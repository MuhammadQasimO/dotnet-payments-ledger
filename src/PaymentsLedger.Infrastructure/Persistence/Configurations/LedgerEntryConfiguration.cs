using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PaymentsLedger.Domain.Ledger;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TransactionId).HasColumnName("transaction_id");
        builder.Property(e => e.WalletId).HasColumnName("wallet_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        // Money is a record struct — projected via ComplexProperty (EF Core 8) so SQL
        // aggregation (SUM) targets the bigint column directly and the currency stays
        // attached for the deferred-trigger grouping logic.
        builder.ComplexProperty(e => e.Amount, amount =>
        {
            amount.Property(a => a.MinorUnits).HasColumnName("amount").IsRequired();
            amount.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(e => new { e.WalletId, e.CreatedAt })
            .HasDatabaseName("ix_ledger_entries_wallet_id_created_at");
        builder.HasIndex(e => e.TransactionId)
            .HasDatabaseName("ix_ledger_entries_transaction_id");

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Wallets.Wallet>()
            .WithMany()
            .HasForeignKey(e => e.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
