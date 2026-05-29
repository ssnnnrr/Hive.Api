using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hive.Api.Data;
using Hive.Api.Entities;
using Hive.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Hive.Api.Services;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class GoalsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        private readonly AIService _aiService;

        public GoalsController(HiveDbContext context, AIService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserGoals(long userId)
        {
            var goals = await _context.Goals
                    .Include(g => g.User)
                    .Include(g => g.Tasks)
                        .ThenInclude(t => t.Comments)
                        .ThenInclude(c => c.User)
                    .Include(g => g.Tasks)
                        .ThenInclude(t => t.Completions)
                        .ThenInclude(tc => tc.User)
                    .Include(g => g.Materials)
                        .ThenInclude(m => m.Creator)
                    .Include(g => g.Collaborations)
                        .ThenInclude(c => c.User)
                    .Where(g => g.UserId == userId || g.Collaborations.Any(c => c.UserId == userId))
                    .ToListAsync();

            var res = goals.Select(g => new GoalResponse(
                g.Id,
                g.Title,
                g.Description,
                g.MeasurableResult,
                g.Progress,
                g.TargetDate,
                g.IsSolo,
                g.Type.ToString(),
                g.Tasks.Select(t => new TaskResponse(
                    t.Id,
                    t.Title,
                    t.DueDate,
                    t.Status.ToString(),
                    t.GoalId,
                    g.Title,
                    t.CreatorId,
                    t.AssigneeId,
                    t.ArtifactUrl,
                    t.StudentComment,
                    t.TeacherComment,
                    // ИСПРАВЛЕНИЕ ТУТ: Создаем объект UserMinimalDto вместо просто строки
                    t.Completions.Select(tc => new UserMinimalDto(
                        tc.User?.Username ?? "Аноним",
                        tc.User?.AvatarUrl
                    )).ToList(),
                    t.Comments.Select(c => new TaskCommentDto(
                        c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt
                    )).ToList()
                )).ToList(),
                g.Collaborations.Select(c => {
                    var partnerTasksDone = g.Tasks.Count(t => t.Completions.Any(tc => tc.UserId == c.UserId));
                    double partnerProgress = g.Tasks.Any() ? (double)partnerTasksDone / g.Tasks.Count * 100 : 0;

                    return new GoalPartnerDto(
                        c.UserId,
                        c.User?.Username ?? "Удален",
                        partnerProgress,
                        c.User?.AvatarUrl,
                        c.IsConfirmed,
                        c.IsAdmin
                    );
                }).ToList(),
                g.Materials.Select(m => new MaterialDto(
                    m.Id, m.Title, m.Content, m.Type.ToString(), m.CreatorId,
                    m.Creator?.Username ?? "Система",
                    m.Creator?.AvatarUrl,
                    m.CreatedAt
                )).ToList(),
                g.UserId
            ));

            return Ok(res);
        }


        [HttpDelete("{goalId}/members/{memberId}")]
        public async Task<IActionResult> RemoveMember(long goalId, long memberId)
        {
            // 1. Находим оригинальную цель со всеми потрохами
            var originalGoal = await _context.Goals
                .Include(g => g.Tasks)
                .Include(g => g.Materials)
                .Include(g => g.Collaborations)
                .FirstOrDefaultAsync(g => g.Id == goalId);

            if (originalGoal == null || originalGoal.UserId != CurrentUserId)
                return Forbid("Только создатель может исключать участников.");

            var collaboration = originalGoal.Collaborations.FirstOrDefault(c => c.UserId == memberId);
            if (collaboration == null) return NotFound("Участник не найден в этой цели.");

            // 2. СОЗДАЕМ «КСЕРОКОПИЮ» (ФОРК) ДЛЯ УДАЛЯЕМОГО УЧАСТНИКА
            var forkedGoal = new Goal
            {
                UserId = memberId, // Теперь он хозяин
                Title = originalGoal.Title + " (Личный)",
                Description = originalGoal.Description,
                MeasurableResult = originalGoal.MeasurableResult,
                TargetDate = originalGoal.TargetDate,
                IsSolo = true, // Становится личным
                Type = GoalType.Social,
                Progress = 0, // Прогресс пересчитается ниже
                CreatedAt = DateTime.UtcNow
            };
            _context.Goals.Add(forkedGoal);
            await _context.SaveChangesAsync();

            // 3. КОПИРУЕМ МАТЕРИАЛЫ
            foreach (var material in originalGoal.Materials)
            {
                _context.Materials.Add(new Material
                {
                    GoalId = forkedGoal.Id,
                    Title = material.Title,
                    Content = material.Content,
                    Type = material.Type,
                    CreatorId = material.CreatorId, // Кто создал оригинал, тот и числится автором
                    CreatedAt = material.CreatedAt
                });
            }

            // 4. КОПИРУЕМ ЗАДАЧИ И ВЫПОЛНЕНИЕ
            foreach (var task in originalGoal.Tasks)
            {
                var newTask = new HiveTask
                {
                    GoalId = forkedGoal.Id,
                    Title = task.Title,
                    DueDate = task.DueDate,
                    Status = task.Status,
                    CreatorId = task.CreatorId
                };
                _context.Tasks.Add(newTask);
                await _context.SaveChangesAsync(); // Сохраняем, чтобы получить newTask.Id

                // Копируем Комментарии к этой задаче
                var comments = await _context.TaskComments.Where(c => c.TaskId == task.Id).ToListAsync();
                foreach (var comm in comments)
                {
                    _context.TaskComments.Add(new TaskComment
                    {
                        TaskId = newTask.Id,
                        UserId = comm.UserId,
                        Text = comm.Text,
                        CreatedAt = comm.CreatedAt
                    });
                }

                // Копируем «Галочку», если этот участник её ставил
                var completion = await _context.TaskCompletions
                    .FirstOrDefaultAsync(tc => tc.TaskId == task.Id && tc.UserId == memberId);

                if (completion != null)
                {
                    _context.TaskCompletions.Add(new TaskCompletion
                    {
                        TaskId = newTask.Id,
                        UserId = memberId,
                        CompletedAt = completion.CompletedAt
                    });
                }
            }

            // 5. УДАЛЯЕМ УЧАСТНИКА ИЗ ОРИГИНАЛЬНОЙ ГРУППЫ
            // Комментарии и материалы участника в ОРИГИНАЛЕ не удаляются! 
            // Они просто остаются там как часть истории, созданная этим пользователем.
            _context.GoalCollaborations.Remove(collaboration);

            await _context.SaveChangesAsync();
            return Ok();
        }


        [HttpPost("{id}/make-solo")]
        public async Task<IActionResult> MakeSolo(long id)
        {
            var goal = await _context.Goals
                .Include(g => g.Collaborations)
                .Include(g => g.Tasks)
                .Include(g => g.Materials)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (goal == null || goal.UserId != CurrentUserId) return Forbid();

            // 1. Находим всех партнеров, которые были в этой цели
            var partners = goal.Collaborations.Where(c => c.UserId != CurrentUserId && c.IsConfirmed).ToList();

            // 2. Для каждого партнера создаем его личную копию этой цели (Логика Fork)
            foreach (var p in partners)
            {
                var newGoal = new Goal
                {
                    UserId = p.UserId,
                    Title = goal.Title + " (Личная)",
                    Description = goal.Description,
                    IsSolo = true,
                    Type = GoalType.Social,
                    TargetDate = goal.TargetDate,
                    Progress = 0, // У нового владельца свой путь
                    CreatedAt = DateTime.UtcNow
                };
                _context.Goals.Add(newGoal);
                await _context.SaveChangesAsync();

                // Копируем задачи
                foreach (var t in goal.Tasks)
                {
                    _context.Tasks.Add(new HiveTask
                    {
                        GoalId = newGoal.Id,
                        Title = t.Title,
                        DueDate = t.DueDate,
                        CreatorId = p.UserId,
                        Status = Entities.TaskStatus.ToDo
                    });
                }
            }

            // 3. Оригинальную цель делаем личной для создателя
            goal.IsSolo = true;
            _context.GoalCollaborations.RemoveRange(goal.Collaborations.Where(c => c.UserId != CurrentUserId));

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> CreateGoal(CreateGoalRequest req)
        {
            if (!Enum.TryParse<GoalType>(req.GoalType, out var gType))
                gType = GoalType.Social;

            var goal = new Goal
            {
                Title = req.Title,
                Description = req.Description,
                MeasurableResult = req.MeasurableResult,
                TargetDate = DateTime.SpecifyKind(req.TargetDate, DateTimeKind.Utc),
                UserId = CurrentUserId,
                IsSolo = req.IsSolo,
                Type = gType,
                CreatedAt = DateTime.UtcNow
            };

            _context.Goals.Add(goal);
            await _context.SaveChangesAsync();

            // Если это Группа или Обмен, создатель получает статус админа/учителя
            _context.GoalCollaborations.Add(new GoalCollaboration
            {
                GoalId = goal.Id,
                UserId = CurrentUserId,
                IsConfirmed = true,
                IsAdmin = gType != GoalType.Social
            });

            foreach (var s in req.Steps)
            {
                _context.Tasks.Add(new HiveTask
                {
                    Title = s.Title,
                    DueDate = DateTime.SpecifyKind(s.DueDate, DateTimeKind.Utc),
                    GoalId = goal.Id,
                    CreatorId = CurrentUserId,
                    Status = Entities.TaskStatus.ToDo
                });
            }

            await _context.SaveChangesAsync();
            return Ok(goal.Id);
        }

        // api/GoalsController.cs

        [HttpPost("materials")]
        public async Task<IActionResult> AddMaterial([FromBody] AddMaterialRequest req)
        {
            var goal = await _context.Goals
                .Include(g => g.Collaborations)
                .FirstOrDefaultAsync(g => g.Id == req.GoalId);

            if (goal == null) return NotFound();

            // Проверка: добавлять может создатель ИЛИ подтвержденный партнер
            bool isPartner = goal.Collaborations.Any(c => c.UserId == CurrentUserId && c.IsConfirmed);
            if (goal.UserId != CurrentUserId && !isPartner) return Forbid();

            var material = new Material
            {
                GoalId = req.GoalId,
                Title = req.Title,
                Content = req.Content,
                Type = Enum.TryParse<MaterialType>(req.Type, out var t) ? t : MaterialType.Link,
                TaskId = req.TaskId,
                CreatorId = CurrentUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Materials.Add(material);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("materials/{id}")]
        public async Task<IActionResult> DeleteMaterial(long id)
        {
            var material = await _context.Materials.FindAsync(id);
            if (material == null) return NotFound();

            // Удалять может только автор материала ИЛИ создатель всей цели
            var goal = await _context.Goals.FindAsync(material.GoalId);
            if (material.CreatorId != CurrentUserId && goal?.UserId != CurrentUserId)
                return Forbid("Вы не можете удалить чужой материал.");

            _context.Materials.Remove(material);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("materials/{id}")]
        public async Task<IActionResult> UpdateMaterial(long id, [FromBody] AddMaterialRequest req)
        {
            var m = await _context.Materials.FindAsync(id);
            if (m == null) return NotFound();
            if (m.CreatorId != CurrentUserId) return Forbid("Редактировать можно только свои материалы.");

            m.Title = req.Title;
            m.Content = req.Content;
            m.TaskId = req.TaskId;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("{goalId}/invite/{partnerId}")]
        public async Task<IActionResult> InvitePartner(long goalId, long partnerId)
        {
            var goal = await _context.Goals.FindAsync(goalId);
            if (goal == null || goal.UserId != CurrentUserId) return Forbid();

            var alreadyInvited = await _context.GoalCollaborations
                .AnyAsync(gc => gc.GoalId == goalId && gc.UserId == partnerId);

            if (alreadyInvited) return BadRequest("Уже в команде");

            _context.GoalCollaborations.Add(new GoalCollaboration
            {
                GoalId = goalId,
                UserId = partnerId,
                IsConfirmed = false
            });

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("invitation/{goalId}/respond")]
        public async Task<IActionResult> RespondToInvite(long goalId, [FromQuery] bool accept)
        {
            var collab = await _context.GoalCollaborations
                .FirstOrDefaultAsync(gc => gc.GoalId == goalId && gc.UserId == CurrentUserId);

            if (collab == null) return NotFound();

            if (accept) collab.IsConfirmed = true;
            else _context.GoalCollaborations.Remove(collab);

            await _context.SaveChangesAsync();
            return Ok();
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var goal = await _context.Goals.Include(g => g.Tasks).FirstOrDefaultAsync(g => g.Id == id);
            if (goal == null || goal.UserId != CurrentUserId) return Forbid();
            _context.Tasks.RemoveRange(goal.Tasks);
            _context.Goals.Remove(goal);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPatch("{id}/toggle-solo")]
        public async Task<IActionResult> ToggleSolo(long id, [FromBody] bool isSolo)
        {
            var goal = await _context.Goals.FindAsync(id);
            if (goal == null || goal.UserId != CurrentUserId) return Forbid();

            goal.IsSolo = isSolo;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("generate-draft")]
        public async Task<IActionResult> GenerateDraft([FromBody] SmartGoalRequest req)
        {
            var steps = await _aiService.GenerateTasksAsync(req);
            return Ok(steps);
        }
    }
}