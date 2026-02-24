using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.DTO_S.AuthDto.Requests
{
    public class LoginRequest
    {
        [MaxLength(10)]
        [MinLength(10)]
       public string PhoneNumber { get; set; }
        
    }
}
