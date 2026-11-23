using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

[ApiController]
[Route("api/halls")]
public class HallController : ControllerBase
{
    private readonly HallService _hallService;

    public HallController(HallService hallService) => _hallService = hallService;

    [HttpGet]
    public async Task<ActionResult<List<HallResponse>>> GetAll()
        => Ok(await _hallService.GetAllAsync());

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<HallResponse>> Create(HallRequest request)
    {
        var hall = await _hallService.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, hall);
    }
}
