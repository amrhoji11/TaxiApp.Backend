using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Core.DTO_S.AuthDto.Responses
{
    public class RegisterDriverResponse
    {
        public string UserId { get; set; }

        public string FullName { get; set; }
        public string? Address { get; set; }
        public string? ProfilePhotoUrl { get; set; }

        public DriverStatus Status { get; set; }

        public decimal? LastLat { get; set; }
        public decimal? LastLng { get; set; }

        public DateTime? LastSeenAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
