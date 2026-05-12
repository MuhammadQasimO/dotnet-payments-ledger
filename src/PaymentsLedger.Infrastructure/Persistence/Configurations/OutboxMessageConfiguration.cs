using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PaymentsLedger.Infrastructure.Outbox;

namespace PaymentsLedger.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
        builder.Property(m => m.AggregateType).HasColumnName("aggregate_type").HasMaxLength(120).IsRequired();
        builder.Property(m => m.AggregateId).HasColumnName("aggregate_id");
        builder.Property(m => m.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(m => m.Attempts).HasColumnName("attempts");
        builder.Property(m => m.SentAt).HasColumnName("sent_at");
        builder.Property(m => m.DeadLetter).HasColumnName("dead_letter");
        builder.Property(m => m.LastError).HasColumnName("last_error");

        // Single covering index for the dispatcher's poll query.
        builder.HasIndex(m => new { m.SentAt, m.DeadLetter, m.NextAttemptAt })
            .HasDatabaseName("ix_outbox_pending");
    }
}
