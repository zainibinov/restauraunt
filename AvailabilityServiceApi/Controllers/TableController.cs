using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

[ApiController]
public class TableController : ControllerBase
{
    private readonly TableService _tableService;

    public TableController(TableService tableService) => _tableService = tableService;

    [HttpGet("api/halls/{hallId:guid}/tables")]
    public async Task<ActionResult<List<TableResponse>>> GetByHall(Guid hallId)
        => Ok(await _tableService.GetByHallAsync(hallId));

    [HttpPost("api/halls/{hallId:guid}/tables")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<TableResponse>> Create(Guid hallId, TableRequest request)
    {
        var table = await _tableService.CreateAsync(hallId, request);
        return StatusCode(StatusCodes.Status201Created, table);
    }
}
