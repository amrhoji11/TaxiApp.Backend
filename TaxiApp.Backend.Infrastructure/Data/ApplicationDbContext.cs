using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {

        }
         
       
        protected override void OnModelCreating(ModelBuilder builder)
        {

            base.OnModelCreating(builder);

            builder.Entity<Driver>(entity =>
            {

                entity.HasOne(d => d.User)
                      .WithOne(u => u.Driver)
                      .HasForeignKey<Driver>(d => d.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<Passenger>(entity =>
            {
                entity.HasKey(d => d.UserId);

                entity.HasOne(d => d.User)
                      .WithOne(u => u.Passenger)
                      .HasForeignKey<Passenger>(d => d.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });


            builder.Entity<TripOrder>().HasKey(e => new { e.TripId, e.OrderId });

            builder.Entity<Message>(entity =>
            {
                entity.HasOne(m => m.Sender)
                      .WithMany(u => u.SentMessages)
                      .HasForeignKey(m => m.SenderUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.Receiver)
                      .WithMany(u => u.ReceivedMessages)
                      .HasForeignKey(m => m.ReceiverUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(m => m.Order)
                      .WithMany(m => m.Messages) // أو WithMany(o => o.Messages) إذا عندك ICollection
                      .HasForeignKey(m => m.OrderId)
                      .OnDelete(DeleteBehavior.SetNull);

                // Trip relationship (Optional)
                entity.HasOne(m => m.Trip)
                      .WithMany(m => m.Messages) // أو WithMany(t => t.Messages)
                      .HasForeignKey(m => m.TripId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<UserBlock>(entity =>
            {
                entity.HasOne(x => x.User)
              .WithMany()
              .HasForeignKey(x => x.UserId)
              .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.BlockedBy)
                      .WithMany()
                      .HasForeignKey(x => x.BlockedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<DriverApproval>(entity =>
            {

                // One-to-One: Driver (1) <-> DriverApproval (1)
                entity.HasOne(a => a.Driver)
                      .WithOne() // أو .WithOne(d => d.Approval) إذا أضفت Navigation في Driver
                      .HasForeignKey<DriverApproval>(a => a.DriverId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Many-to-One: Reviewer (1) -> Approvals (Many)
                entity.HasOne(a => a.ReviewedBy)
                      .WithMany() // أو WithMany(u => u.ReviewedApprovals) إذا أضفت Collection
                      .HasForeignKey(a => a.ReviewedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);


            });

            builder.Entity<Rating>(entity =>
            {
                entity.HasIndex(a => new { a.TripId, a.RaterUserId, a.TargetUserId }).IsUnique();
                entity.HasOne(a => a.Trip)
                      .WithMany(a => a.Ratings)
                      .HasForeignKey(a => a.TripId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.Rater)
         .WithMany(r => r.RatingsGiven) // أو WithMany(u => u.RatingsGiven)
         .HasForeignKey(r => r.RaterUserId)
         .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Target)
                      .WithMany(r => r.RatingsReceived) // أو WithMany(u => u.RatingsReceived)
                      .HasForeignKey(r => r.TargetUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<OfficeQueueEntry>(entity =>
            {
                entity.HasKey(q => q.QueueEntryId);

                entity.HasOne(q => q.Driver)
                      .WithMany() // أو WithMany(d => d.OfficeQueueEntries) إذا أضفت ICollection في Driver
                      .HasForeignKey(q => q.DriverId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Index للبحث
                entity.HasIndex(q => new { q.DriverId, q.EnteredAt });

                // أهم قيد: يمنع وجود أكثر من سجل نشط لنفس السائق
                entity.HasIndex(q => q.DriverId)
                      .IsUnique()
                      .HasFilter("[LeftAt] IS NULL");


            });

            builder.Entity<OrderReview>(entity =>
            {
                entity.HasOne(r => r.Order)
                      .WithMany(o => o.Reviews)
                      .HasForeignKey(r => r.OrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.ReviewedBy)
                      .WithMany(u => u.Reviews)
                      .HasForeignKey(r => r.ReviewedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }

        public DbSet<Driver> Drivers { get; set; }
        public DbSet<DriverApproval> DriverApprovals { get; set; }
        public DbSet<DriverLocation> DriverLocations { get; set; }
        public DbSet<Message> Messages { get; set; }

        public DbSet<Notification> Notifications { get; set; }

        public DbSet<OfficeQueueEntry> OfficeQueueEntries { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<OrderReview> OrderReviews { get; set; }
        public DbSet<Passenger> Passengers { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<TripOrder> TripOrders { get; set; }
        public DbSet<UserBlock> UserBlocks { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }










    }
}
