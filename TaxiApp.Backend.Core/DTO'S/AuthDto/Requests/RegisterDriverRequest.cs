using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Core.DTO_S.AuthDto.Requests
{
    public class RegisterDriverRequest 
    {
        [MinLength(3)]
        public string FirstName { get; set; }

        [MinLength(3)]
        public string LastName { get; set; }

        [Phone]
        public string PhoneNumber { get; set; }
   

        public string? Address { get; set; }
        public string? ProfilePhotoUrl { get; set; }


       

    }
}
