using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public UsersController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProfile(long id)
        {
            var user = await _context.Users
                .Include(u => u.UserSkills).ThenInclude(us => us.Skill)
                .Include(u => u.ReviewsReceived).ThenInclude(r => r.Reviewer)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            var request = await _context.ChatRequests
                .FirstOrDefaultAsync(r => (r.SenderId == CurrentUserId && r.ReceiverId == id) ||
                                          (r.SenderId == id && r.ReceiverId == CurrentUserId));

            string status = "None";
            if (request != null) status = request.Status == RequestStatus.Accepted ? "Accepted" : "Pending";

            var rating = user.ReviewsReceived.Any() ? user.ReviewsReceived.Average(r => r.Rating) : 0;

            return Ok(new UserProfileDto(
                user.Id,
                user.Username,
                user.Email,
                user.UserSkills.Select(us => new UserSkillDto(us.SkillId, us.Skill!.Name, us.Type.ToString())).ToList(),
                user.ReviewsReceived.Select(r => new ReviewDto(r.Id, r.Rating, r.Comment, r.Reviewer!.Username, r.CreatedAt)).ToList(),
                Math.Round(rating, 1),
                status,
                user.AvatarUrl // ПЕРЕДАЧА ФОТО
            ));
        }

        [HttpPut("me")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            user.Username = req.Username;

            // Если пришла новая строка Base64, сохраняем её
            if (!string.IsNullOrEmpty(req.AvatarUrl))
            {
                user.AvatarUrl = req.AvatarUrl;
            }

            if (!string.IsNullOrEmpty(req.NewPassword))
            {
                if (req.NewPassword != req.ConfirmPassword) return BadRequest("Пароли не совпадают");
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}