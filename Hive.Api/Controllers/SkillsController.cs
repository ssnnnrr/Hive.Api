using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public SkillsController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet]
        public async Task<IActionResult> GetAllSkills() => Ok(await _context.Skills.ToListAsync());

        [HttpPost("sync")]
        public async Task<IActionResult> SyncSkills([FromBody] SyncSkillsRequest req)
        {
            if (!Enum.TryParse<SkillType>(req.Type, out var skillType))
                return BadRequest("Неверный тип навыка");

            // 1. Получаем текущие навыки этого типа
            var existingUserSkills = await _context.UserSkills
                .Where(us => us.UserId == CurrentUserId && us.Type == skillType)
                .ToListAsync();

            var existingIds = existingUserSkills.Select(s => s.SkillId).ToList();

            // 2. Удаляем те, которых больше нет в новом списке
            var toRemove = existingUserSkills.Where(es => !req.SkillIds.Contains(es.SkillId)).ToList();
            if (toRemove.Any()) _context.UserSkills.RemoveRange(toRemove);

            // 3. Добавляем только те, которых еще нет в базе
            foreach (var skillId in req.SkillIds)
            {
                if (!existingIds.Contains(skillId))
                {
                    _context.UserSkills.Add(new UserSkill
                    {
                        UserId = CurrentUserId,
                        SkillId = skillId,
                        Type = skillType,
                        IsAiVerified = false // Новые навыки по умолчанию не верифицированы
                    });
                }
                // Если навык уже был в базе, мы его НЕ ТРОГАЕМ (IsAiVerified остается прежним)
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPartners([FromQuery] string type, [FromQuery] long? skillId, [FromQuery] string? query)
        {
            var myId = CurrentUserId;

            var mySkills = await _context.UserSkills.Where(us => us.UserId == myId).ToListAsync();
            var myTeachingIds = mySkills.Where(s => s.Type == SkillType.Teaching).Select(s => s.SkillId).ToList();
            var myLearningIds = mySkills.Where(s => s.Type == SkillType.Learning).Select(s => s.SkillId).ToList();

            var usersQuery = _context.Users
                .Include(u => u.UserSkills).ThenInclude(us => us.Skill)
                .Include(u => u.ReviewsReceived)
                .Where(u => u.Id != myId);

            if (!string.IsNullOrEmpty(query))
                usersQuery = usersQuery.Where(u => u.Username.Contains(query) || u.Email.Contains(query));

            if (skillId.HasValue && skillId > 0)
            {
                var sType = Enum.Parse<SkillType>(type);
                usersQuery = usersQuery.Where(u => u.UserSkills.Any(us => us.SkillId == skillId && us.Type == sType));
            }

            var users = await usersQuery.ToListAsync();

            var result = users.Select(u =>
            {
                var uTeaching = u.UserSkills.Where(s => s.Type == SkillType.Teaching).ToList();
                var uLearning = u.UserSkills.Where(s => s.Type == SkillType.Learning).ToList();

                var giveSkills = myTeachingIds.Intersect(uLearning.Select(s => s.SkillId))
                    .Select(id => uLearning.First(s => s.SkillId == id).Skill.Name).ToList();

                var takeSkills = uTeaching.Select(s => s.SkillId).Intersect(myLearningIds)
                    .Select(id => uTeaching.First(s => s.SkillId == id).Skill.Name).ToList();

                string synergyLevel = "None";
                if (giveSkills.Any() && takeSkills.Any()) synergyLevel = "Ideal";
                else if (giveSkills.Any() || takeSkills.Any()) synergyLevel = "Match";

                // РАСЧЕТ РЕЙТИНГА С ЗАЩИТОЙ ОТ ОШИБОК
                double avgRating = u.ReviewsReceived.Any() ? u.ReviewsReceived.Average(r => (double)r.Rating) : 0;

                // Если результат расчета не число (NaN) или бесконечность — ставим 0
                if (double.IsNaN(avgRating) || double.IsInfinity(avgRating)) avgRating = 0;

                return new UserDto(
                    u.Id,
                    u.Username,
                    u.Email,
                    synergyLevel,
                    u.AvatarUrl,
                    giveSkills,
                    takeSkills,
                    u.UserSkills.Any(us => us.IsAiVerified && us.Type == SkillType.Teaching),
                    Math.Round(avgRating, 1) // Округление до 1 знака
                );
            })
            .OrderByDescending(u => u.IsVerified)
            .ThenByDescending(u => u.Rating)
            .ToList();

            return Ok(result);
        }
    }

}