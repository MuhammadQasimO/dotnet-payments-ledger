using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_transactions_idempotency_key");

        // The Transaction aggregate owns its entries via the explicit transaction_id
        // FK on ledger_entries. We don't model the collection here as a navigation —
        // entries are loaded explicitly by repositories to keep aggregate boundaries clean.
        builder.Ignore(t => t.Entries);
    }
}
