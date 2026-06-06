using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Hive.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly HiveDbContext _context;
        private readonly AIService _aiService;

        public VerificationController(HiveDbContext context, AIService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);


        [HttpGet("get-test/{skillId}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // ИСПРАВЛЕНО: Запрет кэша
        public async Task<IActionResult> GetVerificationTest(long skillId)
        {
            try
            {
                var skill = await _context.Skills.FindAsync(skillId);
                if (skill == null) return NotFound(new { message = "Навык не найден" });

                // Мы передаем Random Guid или метку времени в промпт (внутри сервиса), 
                // чтобы ИИ не выдавал старый результат из своего кэша
                var testJson = await _aiService.GenerateComplexVerificationTest(skill.Name);

                if (string.IsNullOrEmpty(testJson))
                {
                    return BadRequest(new { message = "ИИ не смог сформировать тест. Попробуйте еще раз." });
                }

                return Ok(new { skillId, testData = testJson });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        [HttpPost("submit-result")]
        public async Task<IActionResult> SubmitResult([FromBody] VerificationSubmitRequest req)
        {
            // Проверка: тест сдан, если результат 80% и выше
            if (req.Score >= 0.8)
            {
                var userSkill = await _context.UserSkills
                    .FirstOrDefaultAsync(us => us.UserId == CurrentUserId && us.SkillId == req.SkillId);

                if (userSkill != null)
                {
                    userSkill.IsAiVerified = true;
                    userSkill.VerifiedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return Ok(new { isVerified = true });
                }
            }
            return Ok(new { isVerified = false, message = "Недостаточно баллов для верификации" });
        }
    }
}