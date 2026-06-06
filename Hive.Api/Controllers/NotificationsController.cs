using Hive.Api.Data;
using Hive.Api.Entities;
using Hive.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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
        private readonly IHubContext<ChatHub> _hubContext;

        public NotificationsController(HiveDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet]
        public async Task<IActionResult> GetMyNotifications()
        {
            // 1. УЧЕТ МОСКОВСКОГО ВРЕМЕНИ (UTC+3)
            var nowUtc = DateTime.UtcNow;
            var moscowNow = nowUtc.AddHours(3);
            var todayMoscow = moscowNow.Date;

            var existingNotes = await _context.Notifications
                .Where(n => n.UserId == CurrentUserId && !n.IsRead)
                .ToListAsync();

            // --- ОЧИСТКА РЕШЕННЫХ УВЕДОМЛЕНИЙ ---
            var overdueTypes = new[] { "EventOverdue", "TaskOverdue", "RoadmapOverdue" };
            var notesToDelete = new List<Notification>();

            foreach (var note in existingNotes.Where(n => overdueTypes.Contains(n.Type)))
            {
                if (long.TryParse(note.Data, out long entityId))
                {
                    bool isResolved = false;
                    if (note.Type == "EventOverdue")
                        isResolved = await _context.Events.AnyAsync(e => e.Id == entityId && (e.IsCompleted || e.EventDate >= nowUtc));
                    else if (note.Type == "TaskOverdue")
                        isResolved = await _context.Tasks.AnyAsync(t => t.Id == entityId && (t.Status == Entities.TaskStatus.Done || t.DueDate.AddHours(3).Date >= todayMoscow));
                    else if (note.Type == "RoadmapOverdue")
                        isResolved = await _context.RoadmapSteps.AnyAsync(s => s.Id == entityId && (s.Status == Entities.TaskStatus.Done || s.DueDate.AddHours(3).Date >= todayMoscow));

                    if (isResolved) notesToDelete.Add(note);
                }
            }
            if (notesToDelete.Any()) _context.Notifications.RemoveRange(notesToDelete);

            // --- ГЕНЕРАЦИЯ ПРОСРОЧЕК ---

            // 1. СОБЫТИЯ (Просрочено минута в минуту по UTC)
            var overdueEvents = await _context.Events
                .Where(e => e.CreatorId == CurrentUserId && !e.IsCompleted && e.EventDate < nowUtc)
                .ToListAsync();

            foreach (var ev in overdueEvents)
            {
                if (!existingNotes.Any(n => n.Type == "EventOverdue" && n.Data == ev.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Событие пропущено! ⚠️",
                        Message = $"Вы пропустили время: {ev.Title}",
                        CreatedAt = nowUtc,
                        Type = "EventOverdue",
                        Data = ev.Id.ToString()
                    });
                }
            }

            // 2. ШАГИ К ЦЕЛИ (Просрочено, если день дедлайна по МСК прошел)
            var overdueTasks = await _context.Tasks
                .Where(t => t.CreatorId == CurrentUserId && t.Status != Entities.TaskStatus.Done && t.DueDate.AddHours(3).Date < todayMoscow)
                .ToListAsync();

            foreach (var task in overdueTasks)
            {
                if (!existingNotes.Any(n => n.Type == "TaskOverdue" && n.Data == task.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Шаг к цели пропущен! ✍️",
                        Message = $"Дедлайн истек: {task.Title}",
                        CreatedAt = nowUtc,
                        Type = "TaskOverdue",
                        Data = task.Id.ToString()
                    });
                }
            }

            // 3. ЗАДАНИЯ ИЗ ЧАТОВ (Roadmap)
            var myGroupIds = await _context.GroupMembers.Where(gm => gm.UserId == CurrentUserId).Select(gm => gm.GroupId).ToListAsync();
            var overdueSteps = await _context.RoadmapSteps
                .Where(s => myGroupIds.Contains(s.GroupId) && s.CreatorId != CurrentUserId && s.Status != Entities.TaskStatus.Done && s.DueDate.AddHours(3).Date < todayMoscow)
                .ToListAsync();

            foreach (var step in overdueSteps)
            {
                if (!existingNotes.Any(n => n.Type == "RoadmapOverdue" && n.Data == step.Id.ToString()))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Задание просрочено! 🔔",
                        Message = $"Срок сдачи вышел: {step.Content}",
                        CreatedAt = nowUtc,
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
            _context.Notifications.Remove(note);
            await _context.SaveChangesAsync();
            await _hubContext.Clients.User(CurrentUserId.ToString()).SendAsync("NotificationDeleted", id);
            return Ok();
        }
    }
}