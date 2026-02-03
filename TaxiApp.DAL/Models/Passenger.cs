using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.DAL.Models
{
    public class Passenger
    {
        [Key]
        public string UserId { get; set; }
        public string?  ProfilePhotoUrl { get; set; }
        public string?  Address { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public ApplicationUser User { get; set; }
        public ICollection<Order> Orders { get; set; } = new List<Order>();

    }
}
