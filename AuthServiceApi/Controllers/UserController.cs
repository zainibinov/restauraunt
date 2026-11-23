using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthServiceApi;

[ApiController]
[Authorize]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;

    public UserController(UserService userService) => _userService = userService;

    [HttpGet("me")]
    public async Task<ActionResult<UserInfoResponse>> GetMyInfo()
        => Ok(await _userService.GetAsync(User.GetUserId()));

    [HttpPut("me")]
    public async Task<ActionResult<UserInfoResponse>> UpdateMyProfile(UpdateProfileRequest request)
        => Ok(await _userService.UpdateProfileAsync(User.GetUserId(), request));

    [HttpGet("{userId:guid}")]
    [Authorize(Roles = $"{RoleNames.Waiter},{RoleNames.Admin}")]
    public async Task<ActionResult<UserInfoResponse>> GetUser(Guid userId)
        => Ok(await _userService.GetAsync(userId));
}
