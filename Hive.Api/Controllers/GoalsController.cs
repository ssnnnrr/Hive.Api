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
            // Id того, кто сейчас авторизован (через токен)
            var currentRequestUserId = CurrentUserId;

            var goals = await _context.Goals
                    .Include(g => g.User)
                    .Include(g => g.Tasks).ThenInclude(t => t.Completions)
                    .Include(g => g.Collaborations).ThenInclude(c => c.User)
                    .Include(g => g.Materials).ThenInclude(m => m.Task)
                    .Where(g => g.UserId == userId || g.Collaborations.Any(c => c.UserId == userId))
                    .ToListAsync();

            var res = goals.Select(g => {
                // РАСЧЕТ ПЕРСОНАЛЬНОГО ПРОГРЕССА ДЛЯ ТЕКУЩЕГО ПОЛЬЗОВАТЕЛЯ
                // Считаем сколько задач выполнил именно этот юзер в этой цели
                var myDoneTasksCount = g.Tasks.Count(t => t.Completions.Any(tc => tc.UserId == currentRequestUserId));
                double personalProgress = g.Tasks.Any()
                    ? (double)myDoneTasksCount / g.Tasks.Count * 100
                    : 0;

                // Расчет прогресса для других участников команды (для блока Команда)
                var collaborationsWithProgress = g.Collaborations.Select(c => {
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
                }).ToList();

                return new GoalResponse(
                    g.Id,
                    g.Title,
                    g.Description,
                    g.MeasurableResult,
                    personalProgress, // ТЕПЕРЬ ТУТ ВСЕГДА ЛИЧНЫЙ ПРОГРЕСС
                    g.TargetDate,
                    g.IsSolo,
                    g.Type.ToString(),
                    g.Tasks.Select(t => new TaskResponse(
                        t.Id, t.Title, t.DueDate, t.Status.ToString(), t.GoalId, g.Title,
                        t.CreatorId, t.AssigneeId, t.ArtifactUrl, t.StudentComment, t.TeacherComment,
                        t.Completions.Select(tc => new UserMinimalDto(tc.User?.Username ?? "Аноним", tc.User?.AvatarUrl)).ToList(),
                        t.Comments.Select(c => new TaskCommentDto(c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt)).ToList(),
                        g.IsSolo
                    )).ToList(),
                    collaborationsWithProgress,
                    g.Materials.Select(m => new MaterialDto(
                        m.Id, m.Title, m.Content, m.Type.ToString(), m.CreatorId, m.Creator?.Username ?? "Система",
                        m.Creator?.AvatarUrl, m.CreatedAt, (int?)m.TaskId, m.Task?.Title
                    )).ToList(),
                    g.UserId
                );
            });

            return Ok(res);
        }

        [HttpDelete("{goalId}/members/{memberId}")]
        public async Task<IActionResult> RemoveMember(long goalId, long memberId)
        {
            var originalGoal = await _context.Goals
                .Include(g => g.Tasks).ThenInclude(t => t.Comments)
                .Include(g => g.Tasks).ThenInclude(t => t.Completions)
                .Include(g => g.Materials)
                .Include(g => g.Collaborations)
                .FirstOrDefaultAsync(g => g.Id == goalId);

            if (originalGoal == null || originalGoal.UserId != CurrentUserId)
                return Forbid("Только создатель может исключать участников.");

            var collaboration = originalGoal.Collaborations.FirstOrDefault(c => c.UserId == memberId);
            if (collaboration == null) return NotFound("Участник не найден.");

            if (collaboration.IsConfirmed)
            {
                var forkedGoal = new Goal
                {
                    UserId = memberId,
                    Title = originalGoal.Title + " (Личный)",
                    Description = originalGoal.Description,
                    MeasurableResult = originalGoal.MeasurableResult,
                    TargetDate = originalGoal.TargetDate,
                    IsSolo = true,
                    Type = GoalType.Social,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Goals.Add(forkedGoal);
                await _context.SaveChangesAsync();

                foreach (var m in originalGoal.Materials)
                {
                    _context.Materials.Add(new Material
                    {
                        GoalId = forkedGoal.Id,
                        Title = m.Title,
                        Content = m.Content,
                        Type = m.Type,
                        CreatorId = m.CreatorId,
                        CreatedAt = m.CreatedAt,
                        TaskId = m.TaskId
                    });
                }

                foreach (var oldTask in originalGoal.Tasks)
                {
                    var newTask = new HiveTask
                    {
                        GoalId = forkedGoal.Id,
                        Title = oldTask.Title,
                        DueDate = oldTask.DueDate,
                        Status = Entities.TaskStatus.ToDo,
                        CreatorId = oldTask.CreatorId,
                        AssigneeId = oldTask.AssigneeId,
                        ArtifactUrl = oldTask.ArtifactUrl,
                        StudentComment = oldTask.StudentComment,
                        TeacherComment = oldTask.TeacherComment
                    };
                    _context.Tasks.Add(newTask);
                    await _context.SaveChangesAsync();

                    foreach (var comm in oldTask.Comments)
                    {
                        _context.TaskComments.Add(new TaskComment
                        {
                            TaskId = newTask.Id,
                            UserId = comm.UserId,
                            Text = comm.Text,
                            CreatedAt = comm.CreatedAt
                        });
                    }

                    var userDone = oldTask.Completions.FirstOrDefault(tc => tc.UserId == memberId);
                    if (userDone != null)
                    {
                        _context.TaskCompletions.Add(new TaskCompletion
                        {
                            TaskId = newTask.Id,
                            UserId = memberId,
                            CompletedAt = userDone.CompletedAt
                        });
                        newTask.Status = Entities.TaskStatus.Done;
                    }
                }

                var memberCompletions = originalGoal.Tasks
                    .SelectMany(t => t.Completions)
                    .Where(tc => tc.UserId == memberId)
                    .ToList();

                _context.TaskCompletions.RemoveRange(memberCompletions);
            }

            _context.GoalCollaborations.Remove(collaboration);

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.UserId == memberId &&
                                           n.Type == "GoalInvite" &&
                                           n.Data == goalId.ToString());
            if (notification != null)
            {
                _context.Notifications.Remove(notification);
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("{id}/make-solo")]
        public async Task<IActionResult> MakeSolo(long id)
        {
            var goal = await _context.Goals
                .Include(g => g.Collaborations)
                .Include(g => g.Tasks)
                    .ThenInclude(t => t.Completions)
                .Include(g => g.Tasks)
                    .ThenInclude(t => t.Comments)
                .Include(g => g.Materials)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (goal == null || goal.UserId != CurrentUserId) return Forbid();

            var confirmedPartners = goal.Collaborations
                .Where(c => c.UserId != CurrentUserId && c.IsConfirmed)
                .ToList();

            var unconfirmedPartners = goal.Collaborations
                .Where(c => c.UserId != CurrentUserId && !c.IsConfirmed)
                .ToList();

            foreach (var p in confirmedPartners)
            {
                var newGoal = new Goal
                {
                    UserId = p.UserId,
                    Title = goal.Title + " (Личная)",
                    Description = goal.Description,
                    MeasurableResult = goal.MeasurableResult,
                    IsSolo = true,
                    Type = GoalType.Social,
                    TargetDate = goal.TargetDate,
                    Progress = 0,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Goals.Add(newGoal);
                await _context.SaveChangesAsync();

                foreach (var m in goal.Materials)
                {
                    _context.Materials.Add(new Material
                    {
                        GoalId = newGoal.Id,
                        Title = m.Title,
                        Content = m.Content,
                        Type = m.Type,
                        CreatorId = m.CreatorId,
                        CreatedAt = m.CreatedAt,
                        TaskId = m.TaskId
                    });
                }

                foreach (var t in goal.Tasks)
                {
                    var newTask = new HiveTask
                    {
                        GoalId = newGoal.Id,
                        Title = t.Title,
                        DueDate = t.DueDate,
                        CreatorId = p.UserId,
                        Status = Entities.TaskStatus.ToDo,
                        AssigneeId = t.AssigneeId,
                        ArtifactUrl = t.ArtifactUrl,
                        StudentComment = t.StudentComment,
                        TeacherComment = t.TeacherComment
                    };
                    _context.Tasks.Add(newTask);
                    await _context.SaveChangesAsync();

                    foreach (var comm in t.Comments)
                    {
                        _context.TaskComments.Add(new TaskComment
                        {
                            TaskId = newTask.Id,
                            UserId = comm.UserId,
                            Text = comm.Text,
                            CreatedAt = comm.CreatedAt
                        });
                    }

                    var partnerCompletion = t.Completions.FirstOrDefault(tc => tc.UserId == p.UserId);
                    if (partnerCompletion != null)
                    {
                        _context.TaskCompletions.Add(new TaskCompletion
                        {
                            TaskId = newTask.Id,
                            UserId = p.UserId,
                            CompletedAt = partnerCompletion.CompletedAt
                        });
                        newTask.Status = Entities.TaskStatus.Done;
                    }
                }
            }

            var allPartnerIds = goal.Collaborations
                .Where(c => c.UserId != CurrentUserId)
                .Select(c => c.UserId)
                .ToList();

            var completionsToRemove = goal.Tasks
                .SelectMany(t => t.Completions)
                .Where(tc => allPartnerIds.Contains(tc.UserId))
                .ToList();

            _context.TaskCompletions.RemoveRange(completionsToRemove);

            foreach (var p in unconfirmedPartners)
            {
                var notification = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.UserId == p.UserId &&
                                               n.Type == "GoalInvite" &&
                                               n.Data == id.ToString());
                if (notification != null)
                {
                    _context.Notifications.Remove(notification);
                }
            }

            goal.IsSolo = true;

            _context.GoalCollaborations.RemoveRange(
                goal.Collaborations.Where(c => c.UserId != CurrentUserId)
            );

            await _context.SaveChangesAsync();
            return Ok();
        }

        // Hive.Api/Controllers/GoalsController.cs

        [HttpPost("materials/upload")]
        [Consumes("multipart/form-data")] // Указываем тип контента явно
        public async Task<IActionResult> UploadMaterial([FromForm] UploadMaterialRequest req)
        {
            try
            {
                var goal = await _context.Goals
                    .Include(g => g.Collaborations)
                    .FirstOrDefaultAsync(g => g.Id == req.GoalId);

                if (goal == null) return NotFound(new { message = "Цель не найдена" });

                // Проверка прав
                bool isPartner = goal.Collaborations.Any(c => c.UserId == CurrentUserId && c.IsConfirmed);
                if (goal.UserId != CurrentUserId && !isPartner) return Forbid();

                string fileUrl = "";
                if (req.File != null && req.File.Length > 0)
                {
                    // 1. Создаем путь к папке (wwwroot/uploads/materials)
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "materials");

                    // КРИТИЧНО: Проверяем и создаем папку, если её нет
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    // 2. Генерируем уникальное имя
                    var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(req.File.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    // 3. Сохраняем файл на диск
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await req.File.CopyToAsync(stream);
                    }

                    // URL для доступа с фронтенда
                    fileUrl = $"/uploads/materials/{uniqueFileName}";
                }
                else
                {
                    return BadRequest(new { message = "Файл не был передан" });
                }

                // 4. Создаем запись в БД
                var material = new Material
                {
                    GoalId = req.GoalId,
                    Title = req.Title,
                    Content = fileUrl,
                    Type = MaterialType.File,
                    TaskId = req.TaskId,
                    CreatorId = CurrentUserId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                // Подгружаем заголовок задачи для ответа, если она есть
                string? taskTitle = null;
                if (req.TaskId.HasValue)
                {
                    var task = await _context.Tasks.FindAsync(req.TaskId.Value);
                    taskTitle = task?.Title;
                }

                var user = await _context.Users.FindAsync(CurrentUserId);

                return Ok(new MaterialDto(
                    material.Id,
                    material.Title,
                    material.Content,
                    "File",
                    material.CreatorId,
                    user?.Username ?? "Система",
                    user?.AvatarUrl,
                    material.CreatedAt,
                    (int?)material.TaskId,
                    taskTitle
                ));
            }
            catch (Exception ex)
            {
                // Логируем реальную ошибку в консоль сервера
                Console.WriteLine($"[UPLOAD ERROR]: {ex.Message}");
                return StatusCode(500, new { message = "Ошибка на сервере при сохранении файла", detail = ex.Message });
            }
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

        [HttpPost("materials")]
        public async Task<IActionResult> AddMaterial([FromBody] AddMaterialRequest req)
        {
            var goal = await _context.Goals
                .Include(g => g.Collaborations)
                .FirstOrDefaultAsync(g => g.Id == req.GoalId);

            if (goal == null) return NotFound();

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

            string? taskTitle = null;
            if (req.TaskId.HasValue)
            {
                var task = await _context.Tasks.FindAsync(req.TaskId.Value);
                taskTitle = task?.Title;
            }

            var user = await _context.Users.FindAsync(CurrentUserId);

            return Ok(new MaterialDto(
                material.Id,
                material.Title,
                material.Content,
                material.Type.ToString(),
                material.CreatorId,
                user?.Username ?? "Система",
                user?.AvatarUrl,
                material.CreatedAt,
                (int?)material.TaskId,
                taskTitle
            ));
        }

        [HttpDelete("materials/{id}")]
        public async Task<IActionResult> DeleteMaterial(long id)
        {
            var material = await _context.Materials.FindAsync(id);
            if (material == null) return NotFound();

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
            var goal = await _context.Goals
                .Include(g => g.Tasks)
                .FirstOrDefaultAsync(g => g.Id == goalId);

            if (goal == null || goal.UserId != CurrentUserId) return Forbid();

            var inviter = await _context.Users.FindAsync(CurrentUserId);
            string inviterName = inviter?.Username ?? "Твой партнер";

            var alreadyInvited = await _context.GoalCollaborations
                .AnyAsync(gc => gc.GoalId == goalId && gc.UserId == partnerId);

            if (alreadyInvited) return BadRequest("Уже в команде");

            _context.GoalCollaborations.Add(new GoalCollaboration
            {
                GoalId = goalId,
                UserId = partnerId,
                IsConfirmed = false
            });

            string? aiSummary = await _aiService.GenerateGoalInvitationSummaryAsync(
                inviterName,
                goal.Title,
                goal.Description ?? "",
                goal.MeasurableResult ?? ""
            );

            string finalMessage = !string.IsNullOrEmpty(aiSummary)
                ? aiSummary
                : $"{inviterName} приглашает вас в путь: {goal.Title}. Дедлайн: {goal.TargetDate:dd.MM}. Задач: {goal.Tasks.Count}";

            _context.Notifications.Add(new Notification
            {
                UserId = partnerId,
                Title = "Новое приглашение 🎯",
                Message = finalMessage,
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                Type = "GoalInvite",
                Data = goalId.ToString()
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

        [HttpPost("{goalId}/leave")]
        public async Task<IActionResult> LeaveGoal(long goalId)
        {
            var collaboration = await _context.GoalCollaborations
                .FirstOrDefaultAsync(gc => gc.GoalId == goalId && gc.UserId == CurrentUserId);

            if (collaboration == null) return NotFound();

            var goal = await _context.Goals
                .Include(g => g.Tasks)
                    .ThenInclude(t => t.Completions)
                .FirstOrDefaultAsync(g => g.Id == goalId);

            if (goal?.UserId == CurrentUserId)
                return BadRequest("Создатель не может покинуть цель, только удалить её.");

            var userCompletions = goal.Tasks
                .SelectMany(t => t.Completions)
                .Where(tc => tc.UserId == CurrentUserId)
                .ToList();

            _context.TaskCompletions.RemoveRange(userCompletions);

            _context.GoalCollaborations.Remove(collaboration);
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