using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.Models
{
    public enum DriverStatus
    {
        Pending = 0,
        available = 1,
        busy = 2,
        offline = 3
    }
    public class Driver
    {
        [Key]
        public string UserId { get; set; }

        public string? Address { get; set; }
        public string? ProfilePhotoUrl { get; set; }

        public DriverStatus Status { get; set; }

        public decimal? LastLat { get; set; }
        public decimal? LastLng { get; set; }

        public DateTime? LastSeenAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ApplicationUser User { get; set; }

        public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
        public ICollection<Trip> Trips { get; set; }= new List<Trip>();
        public ICollection<DriverLocation> Locations { get; set; } = new List<DriverLocation>();

    }
}
