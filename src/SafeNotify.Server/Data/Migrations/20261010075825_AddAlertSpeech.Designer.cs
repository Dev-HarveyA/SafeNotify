using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SafeNotify.Server.Data;

#nullable disable

namespace SafeNotify.Server.Data.Migrations
{
    [DbContext(typeof(SafeNotifyDbContext))]
    [Migration("20261010075825_AddAlertSpeech")]
    partial class AddAlertSpeech
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("SafeNotify.Server.Data.Administrator", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Email")
                        .IsUnique();

                    b.ToTable("Administrators");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Alert", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Audience")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<int>("CreatedById")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("DeviceId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("ExpiresAt")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Message")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Severity")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("SpeakMessage")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Alerts");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.AuditEntry", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int?>("AdministratorId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Details")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int?>("DeviceId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("AuditEntries");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Delivery", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long?>("AcknowledgedAt")
                        .HasColumnType("INTEGER");

                    b.Property<int>("AlertId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("DeviceId")
                        .HasColumnType("INTEGER");

                    b.Property<long?>("ResolvedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("UpdatedAt")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("DeviceId");

                    b.HasIndex("AlertId", "DeviceId")
                        .IsUnique();

                    b.ToTable("Deliveries");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Device", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("CredentialHash")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int?>("GroupId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("InstallationId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long?>("LastSeenAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("ProtectedCredential")
                        .HasColumnType("TEXT");

                    b.Property<long?>("RevokedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("CredentialHash")
                        .IsUnique();

                    b.HasIndex("GroupId");

                    b.HasIndex("InstallationId")
                        .IsUnique();

                    b.ToTable("Devices");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.DeviceGroup", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("DeviceGroups");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.JoinRequest", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ClaimHash")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long?>("DecidedAt")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("DecidedById")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("DeviceId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("DeviceName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("InstallationId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("RequestedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("InstallationId", "Status");

                    b.ToTable("JoinRequests");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Organisation", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("CreatedAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("Organisations");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Delivery", b =>
                {
                    b.HasOne("SafeNotify.Server.Data.Alert", "Alert")
                        .WithMany()
                        .HasForeignKey("AlertId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("SafeNotify.Server.Data.Device", "Device")
                        .WithMany()
                        .HasForeignKey("DeviceId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Alert");

                    b.Navigation("Device");
                });

            modelBuilder.Entity("SafeNotify.Server.Data.Device", b =>
                {
                    b.HasOne("SafeNotify.Server.Data.DeviceGroup", "Group")
                        .WithMany()
                        .HasForeignKey("GroupId")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.Navigation("Group");
                });
#pragma warning restore 612, 618
        }
    }
}
