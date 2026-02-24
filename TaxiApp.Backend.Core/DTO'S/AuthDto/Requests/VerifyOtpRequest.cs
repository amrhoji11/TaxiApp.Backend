using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.DTO_S.AuthDto.Requests
{
    public class VerifyOtpRequest
    {
        [MinLength(10)]
        [MaxLength(10)]
        public string PhoneNumber { get; set; } // يتم إرساله برمجياً من الفرونت إند
        public string OtpCode { get; set; }      // يدخله المستخدم يدوياً
    }
}
