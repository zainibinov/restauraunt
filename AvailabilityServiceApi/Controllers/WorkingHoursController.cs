using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

[ApiController]
[Route("api/working-hours")]
public class WorkingHoursController : ControllerBase
{
    private readonly WorkingHoursService _workingHoursService;

    public WorkingHoursController(WorkingHoursService workingHoursService)
        => _workingHoursService = workingHoursService;

    [HttpGet]
    public async Task<ActionResult<List<WorkingHoursResponse>>> GetAll()
        => Ok(await _workingHoursService.GetAllAsync());

    [HttpPut]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<WorkingHoursResponse>> Update(WorkingHoursRequest request)
        => Ok(await _workingHoursService.UpdateAsync(request));
}
