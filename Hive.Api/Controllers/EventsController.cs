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
    public class EventsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public EventsController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Создать событие
        [HttpPost]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateEventRequest req)
        {
            // ПРОВЕРКА: Нельзя создавать события в прошлом
            if (req.EventDate < DateTime.UtcNow)
            {
                return BadRequest("Нельзя запланировать событие на прошедшее время.");
            }

            var ev = new Event
            {
                Title = req.Title,
                Description = req.Description,
                EventDate = DateTime.SpecifyKind(req.EventDate, DateTimeKind.Utc),
                CreatorId = CurrentUserId,
                GroupId = req.GroupId,
                LinkUrl = req.LinkUrl,
                Location = req.Location,
                ImageUrl = req.ImageUrl,
                IsCompleted = false
            };

            _context.Events.Add(ev);
            await _context.SaveChangesAsync();

            return Ok(ev.Id);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] CreateEventRequest req)
        {
            var ev = await _context.Events.FindAsync(id);
            if (ev == null) return NotFound();

            // Безопасность: Редактировать может только создатель
            if (ev.CreatorId != CurrentUserId) return Forbid();

            // ПРОВЕРКА: Если дата меняется, она не должна быть в прошлом
            if (req.EventDate != ev.EventDate && req.EventDate < DateTime.UtcNow)
            {
                return BadRequest("Нельзя перенести событие на прошедшее время.");
            }

            ev.Title = req.Title;
            ev.Description = req.Description;
            ev.EventDate = DateTime.SpecifyKind(req.EventDate, DateTimeKind.Utc);
            ev.LinkUrl = req.LinkUrl;
            ev.Location = req.Location;
            ev.ImageUrl = req.ImageUrl;
            ev.GroupId = req.GroupId;

            await _context.SaveChangesAsync();
            return Ok();
        }


        // Изменить статус (выполнено/нет)
        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> ToggleComplete(long id)
        {
            var ev = await _context.Events.FindAsync(id);
            if (ev == null) return NotFound();

            ev.IsCompleted = !ev.IsCompleted;
            await _context.SaveChangesAsync();
            return Ok(ev.IsCompleted);
        }

        // 5. УДАЛЕНИЕ СОБЫТИЯ
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var ev = await _context.Events.FindAsync(id);
            if (ev == null) return NotFound();

            if (ev.CreatorId != CurrentUserId) return Forbid();

            _context.Events.Remove(ev);
            await _context.SaveChangesAsync();
            return Ok();
        }
   


        // Hive.Api/Controllers/EventsController.cs

        [HttpGet("my")]
        public async Task<IActionResult> GetMyEvents()
        {
            var userGroupIds = await _context.GroupMembers
                .Where(gm => gm.UserId == CurrentUserId)
                .Select(gm => gm.GroupId)
                .ToListAsync();

            var events = await _context.Events
                .Include(e => e.Creator)
                .Where(e => e.CreatorId == CurrentUserId || (e.GroupId != null && userGroupIds.Contains(e.GroupId.Value)))
                .OrderBy(e => e.EventDate)
                .ToListAsync();

            var res = events.Select(e => new EventResponse(
                e.Id,
                e.Title,
                e.Description,
                e.EventDate,
                e.IsCompleted,
                e.GroupId,
                e.Creator?.Username ?? "Система",
                e.LinkUrl,
                e.Location,
                e.ImageUrl
            ));

            return Ok(res);
        }

    }
}