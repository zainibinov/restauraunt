using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

[ApiController]
[Route("api/blackout-dates")]
public class BlackoutDateController : ControllerBase
{
    private readonly BlackoutDateService _blackoutService;

    public BlackoutDateController(BlackoutDateService blackoutService) => _blackoutService = blackoutService;

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<BlackoutDateResponse>> Add(BlackoutDateRequest request)
    {
        var item = await _blackoutService.AddAsync(request);
        return StatusCode(StatusCodes.Status201Created, item);
    }

    [HttpGet]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Waiter}")]
    public async Task<ActionResult<List<BlackoutDateResponse>>> GetAll()
        => Ok(await _blackoutService.GetAllAsync());
}
