using System.Diagnostics;
using Acheve.Common.Messages;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;
using StateHolder;

namespace Acheve.Application.Api.Features.Estimations
{
    [ApiController]
    [Route("[controller]")]
    public class EstimationsController : ControllerBase
    {
        private readonly IBus _bus;
        private readonly StateHolderService.StateHolderServiceClient _stateHolderService;
        private readonly ILogger<EstimationsController> _logger;

        public EstimationsController(
            IBus bus,
            StateHolderService.StateHolderServiceClient stateHolderService,
            ILogger<EstimationsController> logger)
        {
            _bus = bus;
            _stateHolderService = stateHolderService;
            _logger = logger;
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("{ticket}")]
        public async Task<ActionResult<EstimationState>> GetEstimationState(Guid ticket)
        {
            var clientId = User.FindFirst("client_id")?.Value;

            var stateResponse = await _stateHolderService.StateQueryAsync(
                new StateRequest { 
                    Ticket = ticket.ToString("D"), 
                    ClientId = clientId 
                });

            return Ok(new EstimationState
            {
                Ticket = stateResponse.Ticket,
                State = stateResponse.State
            });
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost]
        public async Task<ActionResult<NewEstimationResponse>> ProcessNewEstimation([FromBody]NewEstimationRequest request)
        {
            var currentActivity = Activity.Current;
            var clientId = User.Identity?.Name ?? "N/A";

            var message = new EstimationRequested
            {
                CaseNumber = Guid.NewGuid(),
                ClientId = clientId,
                CallbackUri = request.CallBackUri,
                ImageUrls = request.ImageUrls
            };

            await _bus.Send(message);

            return Accepted(new NewEstimationResponse 
            { 
                Token = message.CaseNumber.ToString("D"),
                OperationId = currentActivity?.RootId ?? "N/A"
            });
        }
    }
}
