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

            // --- ЛОГИКА РАСЧЕТА BEEPOWER (Этап 4) ---
            var reviews = user.ReviewsReceived;
            double avgRating = reviews.Any() ? reviews.Average(r => (double)r.Rating) : 0;

            // Считаем "Доводимость" (Completion Rate)
            // Группы, где пользователь был учителем (Owner)
            var totalGroupsAsTeacher = await _context.Groups
                .CountAsync(g => g.OwnerId == id && g.IsSolo);

            // Группы, которые завершены успешно (оба нажали "Завершить")
            var finishedGroups = await _context.Groups
                .CountAsync(g => g.OwnerId == id && g.IsSolo && g.OwnerFinished && g.PartnerFinished);

            double completionRate = totalGroupsAsTeacher > 0
                ? (double)finishedGroups / totalGroupsAsTeacher
                : 0;

            // Финальная формула рейтинга (BeePower) от 0.0 до 5.0
            // 0.7 - вес оценок, 0.3 - вес доводимости (умножаем на 5 для шкалы)
            double calculatedBeePower = (avgRating * 0.7) + (completionRate * 5.0 * 0.3);

            // Статус отношений для отображения кнопок
            var request = await _context.ChatRequests
                .FirstOrDefaultAsync(r => (r.SenderId == CurrentUserId && r.ReceiverId == id) ||
                                          (r.SenderId == id && r.ReceiverId == CurrentUserId));

            string status = "None";
            if (request != null) status = request.Status == RequestStatus.Accepted ? "Accepted" : "Pending";

            return Ok(new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                skills = user.UserSkills.Select(us => new {
                    skillId = us.SkillId,
                    skillName = us.Skill!.Name,
                    type = us.Type.ToString(),
                    isAiVerified = us.IsAiVerified // Этап 3
                }),
                reviews = user.ReviewsReceived.Select(r => new {
                    id = r.Id,
                    rating = r.Rating,
                    comment = r.Comment,
                    reviewerName = r.Reviewer!.Username,
                    createdAt = r.CreatedAt
                }),
                rating = Math.Round(avgRating, 1), // Теперь здесь честный средний балл
                beePower = Math.Round((avgRating * 0.7) + (completionRate * 5.0 * 0.3), 1), // Отдаем BeePower как основной рейтинг
                completionRate = Math.Round(completionRate * 100, 0), // Доп. статистика для профиля
                relationshipStatus = status,
                avatarUrl = user.AvatarUrl
            });
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