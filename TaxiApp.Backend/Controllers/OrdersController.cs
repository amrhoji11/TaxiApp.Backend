using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Passenger")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderRepository orderRepository;
        private readonly IUserBlockRepository userBlockRepository;
        private readonly IUserRepository userRepository;

        public OrdersController(IOrderRepository orderRepository, IUserBlockRepository userBlockRepository,IUserRepository userRepository)
        {
            this.orderRepository = orderRepository;
            this.userBlockRepository = userBlockRepository;
            this.userRepository = userRepository;
        }

        

        

       

        [HttpPost("CreateOrder")]
        public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
        {
            var PassengerId = User.FindFirstValue("UserId");
            if (PassengerId==null)
            {
                return Unauthorized(" لم يتم العثور على هوية المستخدم ");
            }

            if (await userBlockRepository.IsUserBlocked(PassengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            if (!await userRepository.IsUserActive(PassengerId))
                return StatusCode(403, new
                {
                    message = "حسابك غير نشط ، لا يمكنك تنفيذ هذه العملية"
                });

            var result= await orderRepository.CreateOrder(PassengerId,dto);
            if (result==null)
            {
                return BadRequest(result);
            }
            return Ok(result.Adapt<ResponseCreateOrderDto>());


        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAllOrders([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var passengerId = User.FindFirstValue("UserId");

            if (await userBlockRepository.IsUserBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            if (!await userRepository.IsUserActive(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك غير نشط ، لا يمكنك تنفيذ هذه العملية"
                });

            var result = await orderRepository.GetAll(a=>a.PassengerId==passengerId,
                                                      includes: null,  // أو يمكنك إضافة includes إذا تريد جلب العلاقات
                                                      isTracked: false,
                                                      pageNumber: pageNumber,
                                                      pageSize: pageSize);
            if (result==null)
            {
                return NotFound();
            }
            return Ok(result.Adapt<IEnumerable<ResponseCreateOrderDto>>());
        }

        [HttpPut("{id}")]

        public async Task<IActionResult> EditOrder([FromRoute] int id , [FromBody] EditOrderDto dto)
        {
            var passengerId = User.FindFirstValue("UserId");
            if (passengerId == null)
            {
                return Unauthorized(" لم يتم العثور على هوية المستخدم ");
            }

            if (await userBlockRepository.IsUserBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            if (! await userRepository.IsUserActive(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك غير نشط ، لا يمكنك تنفيذ هذه العملية"
                });

            var result = await orderRepository.EditOrder(passengerId, id,dto);


            return Ok(result);

        }

        [HttpPut("{id}/Cancel")]

        public async Task<IActionResult> CancelOrder([FromRoute] int id)
        {
            var passengerId = User.FindFirstValue("UserId");
            if (passengerId == null)
            {
                return Unauthorized(" لم يتم العثور على هوية المستخدم ");
            }

            if (await userBlockRepository.IsUserBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            if (!await userRepository.IsUserActive(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك غير نشط ، لا يمكنك تنفيذ هذه العملية"
                });

            var result = await orderRepository.CancelOrder(passengerId,id);
            return Ok(result);
        }
        




    }
}
