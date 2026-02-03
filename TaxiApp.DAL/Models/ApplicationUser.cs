using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.DAL.Models
{
    public class ApplicationUser:IdentityUser
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Passenger Passenger { get; set; }
        public Driver Driver { get; set; }
        public ICollection<Message> SentMessages { get; set; } = new List<Message>();
        public ICollection<Message> ReceivedMessages { get; set; } = new List<Message>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public ICollection<OrderReview> Reviews { get; set; } = new List<OrderReview>();
        public ICollection<Rating> RatingsGiven { get; set; } = new List<Rating>();
        public ICollection<Rating> RatingsReceived { get; set; } = new List<Rating>();



    }
}
