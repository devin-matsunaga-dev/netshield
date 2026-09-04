using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using NetShield.Inventory.Devices;

namespace NetShield.Inventory.Credentials;

/// <summary>Maps <see cref="DeviceCredentialProfile"/> to <c>device_credential_profiles</c>.</summary>
internal sealed class DeviceCredentialProfileConfiguration
    : IEntityTypeConfiguration<DeviceCredentialProfile>
{
    internal const string TableName = "device_credential_profiles";

    /// <summary>The index that stops one profile being assigned to one device twice.</summary>
    internal const string AssignmentIndexName = "ix_device_credential_profiles_device_id_credential_profile_id";

    public void Configure(EntityTypeBuilder<DeviceCredentialProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(assignment => assignment.Id);

        builder.Property(assignment => assignment.CreatedAt).IsRequired();
        builder.Property(assignment => assignment.UpdatedAt).IsRequired();

        // No navigation properties. Both ends are internal entities in this module, so a
        // navigation would work — but the assignment is read by id on both sides and a navigation
        // is one more way for a query to load a credential profile nobody asked for.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(assignment => assignment.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CredentialProfile>()
            .WithMany()
            .HasForeignKey(assignment => assignment.CredentialProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both directions are queried: what may this device be reached with, and how many devices
        // does this profile cover. CONVENTIONS.md §3 asks for an index on every foreign key.
        builder.HasIndex(assignment => assignment.DeviceId)
            .HasDatabaseName("ix_device_credential_profiles_device_id");

        builder.HasIndex(assignment => assignment.CredentialProfileId)
            .HasDatabaseName("ix_device_credential_profiles_credential_profile_id");

        builder.HasIndex(assignment => new { assignment.DeviceId, assignment.CredentialProfileId })
            .IsUnique()
            .HasDatabaseName(AssignmentIndexName);
    }
}
