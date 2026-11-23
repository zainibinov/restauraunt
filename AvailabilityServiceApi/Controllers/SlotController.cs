using Microsoft.AspNetCore.Mvc;

namespace AvailabilityServiceApi;

[ApiController]
public class SlotController : ControllerBase
{
    private readonly SlotService _slotService;

    public SlotController(SlotService slotService) => _slotService = slotService;

    /// <summary>Example: GET /api/halls/{id}/slots?date=2026-11-20&amp;time=19:00:00&amp;guestsCount=4</summary>
    [HttpGet("api/halls/{hallId:guid}/slots")]
    public async Task<ActionResult<List<SlotResponse>>> GetFreeSlots(
        Guid hallId, [FromQuery] FreeSlotsRequest request)
        => Ok(await _slotService.GetFreeSlotsAsync(hallId, request));

    [HttpGet("api/tables/{tableId:guid}/availability")]
    public async Task<ActionResult<TableAvailabilityResponse>> GetTableAvailability(
        Guid tableId, [FromQuery] DateTime? date)
        => Ok(await _slotService.GetTableAvailabilityAsync(tableId, date));
}
