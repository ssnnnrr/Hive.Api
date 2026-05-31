using Hive.Api.Data;
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
    public class NotificationsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public NotificationsController(HiveDbContext context) => _context = context;
        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet]
        public async Task<IActionResult> GetMyNotifications()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;

            // 1. ПОЛУЧАЕМ ТЕКУЩИЕ НЕПРОЧИТАННЫЕ УВЕДОМЛЕНИЯ
            var existingNotes = await _context.Notifications
                .Where(n => n.UserId == CurrentUserId && !n.IsRead)
                .ToListAsync();

            // --- ЛОГИКА ОЧИСТКИ (Удаляем из БД, если больше не просрочено) ---
            var overdueTypes = new[] { "EventOverdue", "TaskOverdue", "RoadmapOverdue" };
            var notesToDelete = new List<Notification>();

            foreach (var note in existingNotes.Where(n => overdueTypes.Contains(n.Type)))
            {
                if (long.TryParse(note.Data, out long entityId))
                {
                    bool stillOverdue = false;
                    if (note.Type == "EventOverdue")
                        stillOverdue = await _context.Events.AnyAsync(e => e.Id == entityId && !e.IsCompleted && e.EventDate < now);
                    else if (note.Type == "TaskOverdue")
                        stillOverdue = await _context.Tasks.AnyAsync(t => t.Id == entityId && t.Status != Entities.TaskStatus.Done && t.DueDate < today);
                    else if (note.Type == "RoadmapOverdue")
                        stillOverdue = await _context.RoadmapSteps.AnyAsync(s => s.Id == entityId && s.Status != Entities.TaskStatus.Done && s.DueDate < today);

                    if (!stillOverdue) notesToDelete.Add(note);
                }
            }
            if (notesToDelete.Any())
            {
                _context.Notifications.RemoveRange(notesToDelete);
                existingNotes.RemoveAll(n => notesToDelete.Contains(n));
            }

            // --- ЛОГИКА ГЕНЕРАЦИИ (Создаем новые просрочки) ---

            // 1. СОБЫТИЯ (Просрочено, если прошла 1 минута от EventDate)
            var overdueEvents = await _context.Events
                .Where(e => e.CreatorId == CurrentUserId && !e.IsCompleted && e.EventDate < now.AddMinutes(-1))
                .ToListAsync();

            foreach (var ev in overdueEvents)
            {
                if (!existingNotes.Any(n => n.Type == "EventOverdue" && n.Data == ev.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Событие пропущено! ⚠️",
                        Message = $"Вы пропустили: {ev.Title}",
                        CreatedAt = now,
                        Type = "EventOverdue",
                        Data = ev.Id.ToString()
                    });
                }
            }

            // 2. ШАГИ К ЦЕЛИ (Просрочено, если день DueDate уже прошел)
            var overdueTasks = await _context.Tasks
                .Where(t => t.CreatorId == CurrentUserId && t.Status != Entities.TaskStatus.Done && t.DueDate < today)
                .ToListAsync();

            foreach (var task in overdueTasks)
            {
                if (!existingNotes.Any(n => n.Type == "TaskOverdue" && n.Data == task.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Шаг к цели пропущен! ✍️",
                        Message = $"Не выполнено: {task.Title}",
                        CreatedAt = now,
                        Type = "TaskOverdue",
                        Data = task.Id.ToString()
                    });
                }
            }

            // 3. ЗАДАНИЯ ИЗ ЧАТОВ (Roadmap)
            var myGroupIds = await _context.GroupMembers.Where(gm => gm.UserId == CurrentUserId).Select(gm => gm.GroupId).ToListAsync();
            var overdueSteps = await _context.RoadmapSteps
                .Where(s => myGroupIds.Contains(s.GroupId) && s.CreatorId != CurrentUserId && s.Status != Entities.TaskStatus.Done && s.DueDate < today)
                .ToListAsync();

            foreach (var step in overdueSteps)
            {
                if (!existingNotes.Any(n => n.Type == "RoadmapOverdue" && n.Data == step.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Задание из чата пропущено! 🔔",
                        Message = $"Срочно сдай: {step.Content}",
                        CreatedAt = now,
                        Type = "RoadmapOverdue",
                        Data = step.Id.ToString()
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(await _context.Notifications.Where(n => n.UserId == CurrentUserId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt).ToListAsync());
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkAsRead(long id)
        {
            var note = await _context.Notifications.FindAsync(id);
            if (note == null || note.UserId != CurrentUserId) return NotFound();
            note.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}