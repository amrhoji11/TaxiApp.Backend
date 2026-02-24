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

        public OrdersController(IOrderRepository orderRepository, IUserBlockRepository userBlockRepository)
        {
            this.orderRepository = orderRepository;
            this.userBlockRepository = userBlockRepository;
        }

        private async Task<bool> IsBlocked(string userId)
        {
            return await userBlockRepository.IsUserBlocked(userId);

        }

        [HttpPost("CreateOrder")]
        public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
        {
            var PassengerId = User.FindFirstValue("UserId");
            if (PassengerId==null)
            {
                return Unauthorized(" لم يتم العثور على هوية المستخدم ");
            }

            if (await IsBlocked(PassengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            var result= await orderRepository.CreateOrder(PassengerId,dto);
            if (result==null)
            {
                return BadRequest(result);
            }
            return Ok(result.Adapt<ResponseCreateOrderDto>());


        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAllOrders()
        {
            var passengerId = User.FindFirstValue("UserId");

            if (await IsBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });
            var result = await orderRepository.GetAll(a=>a.PassengerId==passengerId);
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

            if (await IsBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
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

            if (await IsBlocked(passengerId))
                return StatusCode(403, new
                {
                    message = "حسابك محظور، لا يمكنك تنفيذ هذه العملية"
                });

            var result = await orderRepository.CancelOrder(passengerId,id);
            return Ok(result);
        }
        




    }
}
