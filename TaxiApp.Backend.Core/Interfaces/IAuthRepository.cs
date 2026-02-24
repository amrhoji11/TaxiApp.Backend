using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S.AuthDto;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Responses;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface IAuthRepository
    {
        Task<string> LoginAsync(LoginRequest request);
        Task<LoginResponse> VerifyOtpAndLoginAsync(VerifyOtpRequest request);
        Task<RegisterPassengerResponse> RegisterPassengerAsync(RegisterPassengerRequest request);
        Task<RegisterDriverResponse> RegisterDriverAsync(RegisterDriverRequest request);
    }
}
