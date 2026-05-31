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
            if (!Enum.TryParse<SkillType>(req.Type, out var skillType)) return BadRequest("Неверный тип навыка");

            var existingSkills = _context.UserSkills.Where(us => us.UserId == CurrentUserId && us.Type == skillType);
            _context.UserSkills.RemoveRange(existingSkills);

            foreach (var skillId in req.SkillIds)
            {
                _context.UserSkills.Add(new UserSkill { UserId = CurrentUserId, SkillId = skillId, Type = skillType });
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPartners([FromQuery] string type, [FromQuery] long? skillId, [FromQuery] string? query)
        {
            var myId = CurrentUserId;

            // Получаем мои навыки для расчета мэтча
            var mySkills = await _context.UserSkills.Where(us => us.UserId == myId).ToListAsync();
            var myTeachingIds = mySkills.Where(s => s.Type == SkillType.Teaching).Select(s => s.SkillId).ToList();
            var myLearningIds = mySkills.Where(s => s.Type == SkillType.Learning).Select(s => s.SkillId).ToList();

            var usersQuery = _context.Users
                .Include(u => u.UserSkills)
                .Include(u => u.ReviewsReceived)
                .Where(u => u.Id != myId);

            // Фильтр по строке
            if (!string.IsNullOrEmpty(query))
                usersQuery = usersQuery.Where(u => u.Username.Contains(query) || u.Email.Contains(query));

            // Фильтр по категории навыка
            if (skillId.HasValue && skillId > 0)
            {
                var sType = Enum.Parse<SkillType>(type);
                usersQuery = usersQuery.Where(u => u.UserSkills.Any(us => us.SkillId == skillId && us.Type == sType));
            }

            var users = await usersQuery.ToListAsync();

            // Внутри метода SearchPartners
            var result = users.Select(u =>
            {
                var uTeaching = u.UserSkills.Where(s => s.Type == SkillType.Teaching).Select(s => s.SkillId).ToList();
                var uLearning = u.UserSkills.Where(s => s.Type == SkillType.Learning).Select(s => s.SkillId).ToList();

                // Проверяем, заполнил ли пользователь хотя бы что-то
                bool hasAnySkills = uTeaching.Any() || uLearning.Any();

                // Synergy: Я учу тому, что он ищет + Он учит тому, что ищу я
                var giveSkills = myTeachingIds.Intersect(uLearning)
                    .Join(_context.Skills, id => id, s => s.Id, (id, s) => s.Name).ToList();
                var takeSkills = uTeaching.Intersect(myLearningIds)
                    .Join(_context.Skills, id => id, s => s.Id, (id, s) => s.Name).ToList();

                bool isIdealMatch = giveSkills.Any() && takeSkills.Any();

                return new UserDto(
                    u.Id,
                    u.Username,
                    u.Email,
                    isIdealMatch ? "Ideal" : "None",
                    u.AvatarUrl,
                    giveSkills, // MatchTeaching
                    takeSkills  // MatchLearning
                )
                {
                    // Можно добавить в модель UserDto поле IsNewcomer или HasSkills
                    // Если у пользователя нет навыков вообще, пометим его
                };
            }).OrderByDescending(u => u.SynergyLevel == "Ideal")
   .ThenBy(u => u.MatchTeaching.Count + u.MatchLearning.Count == 0) // Пустые профили в конец, но они есть
   .ToList();


            return Ok(result);
        }
    }

}