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

            // 1. Авто-генерация уведомлений о просрочке событий
            var overdueEvents = await _context.Events
                .Where(e => e.CreatorId == CurrentUserId && !e.IsCompleted && e.EventDate < now)
                .ToListAsync();

            foreach (var ev in overdueEvents)
            {
                // Уникальный ключ просрочки — тип + ID события, чтобы не дублировать
                var alreadyNotified = await _context.Notifications
                    .AnyAsync(n => n.UserId == CurrentUserId && n.Type == "EventOverdue" && n.Data == ev.Id.ToString());

                if (!alreadyNotified)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Пропущено событие! ⚠️",
                        Message = $"Вы пропустили: {ev.Title}. Нажмите, чтобы перенести.",
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false,
                        Type = "EventOverdue",
                        // ПЕРЕДАЕМ ИМЕННО ДАТУ СОБЫТИЯ для календаря
                        Data = ev.EventDate.ToString("o")
                    });
                }
            }
            var overdueTasks = await _context.Tasks
        .Where(t => t.CreatorId == CurrentUserId && t.Status != Entities.TaskStatus.Done && t.DueDate < today)
        .ToListAsync();

            foreach (var task in overdueTasks)
            {
                var alreadyNotified = await _context.Notifications
                    .AnyAsync(n => n.UserId == CurrentUserId && n.Type == "TaskOverdue" && n.Data == task.Id.ToString());

                if (!alreadyNotified)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Задача пропущена! ✍️",
                        Message = $"Вы не выполнили в срок: {task.Title}",
                        CreatedAt = DateTime.UtcNow,
                        Type = "TaskOverdue",
                        Data = task.DueDate.ToString("o") // Передаем дату для прыжка в календаре
                    });
                }
            }

            // 3. Просрочка по ЗАДАНИЯМ ИЗ ЧАТОВ (RoadmapSteps)
            // Находим группы, где я участник (ученик), и задачи в них от других (учителей)
            var myGroupIds = await _context.GroupMembers.Where(gm => gm.UserId == CurrentUserId).Select(gm => gm.GroupId).ToListAsync();
            var overdueSteps = await _context.RoadmapSteps
                .Where(s => myGroupIds.Contains(s.GroupId) && s.CreatorId != CurrentUserId && s.Status != Entities.TaskStatus.Done && s.DueDate < today)
                .ToListAsync();

            foreach (var step in overdueSteps)
            {
                var alreadyNotified = await _context.Notifications
                    .AnyAsync(n => n.UserId == CurrentUserId && n.Type == "RoadmapOverdue" && n.Data == step.Id.ToString());

                if (!alreadyNotified)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = CurrentUserId,
                        Title = "Задание из чата пропущено! 🔔",
                        Message = $"Срочно сдай: {step.Content}",
                        CreatedAt = DateTime.UtcNow,
                        Type = "RoadmapOverdue",
                        Data = step.DueDate.ToString("o")
                    });
                }
            }

            await _context.SaveChangesAsync();

            // Возвращаем список
            var list = await _context.Notifications
                .Where(n => n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync();
            return Ok(list);
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