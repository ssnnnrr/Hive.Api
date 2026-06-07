using Hive.Api.Data;
using Hive.Api.DTOs;
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
    public class GroupsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public GroupsController(HiveDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var groups = await _context.Groups
                .Include(g => g.Members).ThenInclude(m => m.User)
                .Include(g => g.ChatMessages)
                .Include(g => g.RoadmapSteps)
                .Where(g => g.Members.Any(m => m.UserId == CurrentUserId))
                .Select(g => new GroupResponse
                {
                    Id = g.Id,
                    Name = g.IsSolo
                        ? g.Members.Where(m => m.UserId != CurrentUserId)
                            .Select(m => m.User.Username).FirstOrDefault() ?? "Чат"
                        : g.Name,
                    IsSolo = g.IsSolo,
                    OwnerId = g.OwnerId,
                    MembersCount = g.Members.Count,
                    OtherUserId = g.IsSolo
                        ? g.Members.Where(m => m.UserId != CurrentUserId).Select(m => m.UserId).FirstOrDefault()
                        : null,
                    LastMessage = g.ChatMessages.OrderByDescending(m => m.SentAt).Select(m => m.Content).FirstOrDefault(),
                    LastMessageAt = g.ChatMessages.OrderByDescending(m => m.SentAt).Select(m => m.SentAt).FirstOrDefault(),

                    // Передаем флаги завершения, чтобы фронтенд знал, когда блокировать кнопки
                    OwnerFinished = g.OwnerFinished,
                    PartnerFinished = g.PartnerFinished,

                    UnreadCount = g.ChatMessages.Count(m => m.SenderId != CurrentUserId && !m.IsRead) +
                                  g.RoadmapSteps.Count(s => s.Status != Entities.TaskStatus.Done && s.DueDate < DateTime.UtcNow && s.CreatorId != CurrentUserId)
                })
                .OrderByDescending(g => g.LastMessageAt)
                .ToListAsync();

            return Ok(groups);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetGroup(long id)
        {
            var group = await _context.Groups
                .Include(g => g.Members).ThenInclude(m => m.User)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null) return NotFound();
            if (!group.Members.Any(m => m.UserId == CurrentUserId)) return Forbid();

            return Ok(new GroupDetailResponse
            {
                Id = group.Id,
                Name = group.Name,
                OwnerId = group.OwnerId,
                IsSolo = group.IsSolo,
                OwnerFinished = group.OwnerFinished,
                PartnerFinished = group.PartnerFinished,
                Members = group.Members.Select(m => new UserBriefDto
                {
                    Id = m.UserId,
                    Username = m.User.Username,
                    AvatarUrl = m.User.AvatarUrl
                }).ToList()
            });
        }

        // --- ЛОГИКА ЗАВЕРШЕНИЯ (COMPLETION) ---

        [HttpPost("{groupId}/request-my-completion")]
        public async Task<IActionResult> RequestMyCompletion(long groupId)
        {
            var group = await _context.Groups
                .Include(g => g.RoadmapSteps)
                .Include(g => g.ChatMessages)
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null) return NotFound();

            // 1. ВАЛИДАЦИЯ: Все ли задачи, где я УЧЕНИК (назначены партнером), выполнены?
            var myPendingTasks = group.RoadmapSteps.Any(s =>
                s.GroupId == groupId &&
                s.CreatorId != CurrentUserId &&
                s.Status != Entities.TaskStatus.Done &&
                !s.IsArchived);

            if (myPendingTasks)
                return BadRequest("Вы не можете завершить обучение, пока не выполнены все задачи от вашего учителя.");

            // 2. Очистка старых запросов
            var old = group.ChatMessages.Where(m => m.Content.StartsWith("[COMPLETION_REQUEST]")).ToList();
            _context.ChatMessages.RemoveRange(old);

            // 3. Создание системного сообщения
            var me = await _context.Users.FindAsync(CurrentUserId);
            var systemMessage = new ChatMessage
            {
                GroupId = groupId,
                SenderId = CurrentUserId,
                Content = $"[COMPLETION_REQUEST]|{me.Username} утверждает, что завершил обучение. Учитель, вы подтверждаете?",
                SentAt = DateTime.UtcNow
            };

            _context.ChatMessages.Add(systemMessage);
            await _context.SaveChangesAsync();

            // 1. Создаем DTO для сообщения
            var messageDto = new MessageDto(systemMessage.Id, systemMessage.Content, systemMessage.SenderId, "Система", systemMessage.SentAt, false);

            // 2. ОТПРАВЛЯЕМ СООБЩЕНИЕ (чтобы пузырек появился сразу)
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("ReceiveMessage", messageDto);

            // 3. ОТПРАВЛЯЕМ СИГНАЛ ОБНОВЛЕНИЯ (чтобы обновились кнопки в Roadmap)
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("RoadmapUpdated");

            return Ok();
        }

        [HttpPost("{groupId}/confirm-partner-completion")]
        public async Task<IActionResult> ConfirmPartnerCompletion(long groupId)
        {
            var group = await _context.Groups.Include(g => g.RoadmapSteps).FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return NotFound();

            if (group.OwnerId == CurrentUserId) group.PartnerFinished = true;
            else group.OwnerFinished = true;

            bool isFullyFinished = group.OwnerFinished && group.PartnerFinished;
            if (isFullyFinished)
            {
                var steps = group.RoadmapSteps.Where(s => !s.IsArchived).ToList();
                foreach (var s in steps) s.IsArchived = true;
            }

            await _context.SaveChangesAsync();

            // Оповещаем SignalR
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("RoadmapUpdated");
            return Ok();
        }

        [HttpPost("{groupId}/reject-completion")]
        public async Task<IActionResult> RejectCompletion(long groupId)
        {
            var group = await _context.Groups.Include(g => g.ChatMessages).FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return NotFound();

            // 1. Удаляем запрос на завершение
            var oldMessages = group.ChatMessages.Where(m => m.Content.StartsWith("[COMPLETION_REQUEST]")).ToList();
            _context.ChatMessages.RemoveRange(oldMessages);

            // 2. Добавляем системное сообщение об отказе
            var user = await _context.Users.FindAsync(CurrentUserId);
            _context.ChatMessages.Add(new ChatMessage
            {
                GroupId = groupId,
                SenderId = CurrentUserId,
                Content = $"❌ Учитель считает, что обучение нужно продолжить. План обучения еще не выполнен полностью.",
                SentAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // 3. SignalR: обновляем данные у всех
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("RoadmapUpdated");
            return Ok();
        }

        [HttpPost("{groupId}/confirm-restart")]
        public async Task<IActionResult> ConfirmRestart(long groupId)
        {
            var group = await _context.Groups.Include(g => g.ChatMessages).FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return NotFound();

            group.OwnerFinished = false;
            group.PartnerFinished = false;

            var restarts = group.ChatMessages.Where(m => m.Content.StartsWith("[RESTART_PROPOSAL]")).ToList();
            _context.ChatMessages.RemoveRange(restarts);

            _context.ChatMessages.Add(new ChatMessage
            {
                GroupId = groupId,
                SenderId = CurrentUserId,
                Content = "🔄 Обмен навыками возобновлен! План обучения очищен и готов к новым задачам.",
                SentAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // SignalR: Разблокируем чат у обоих
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("RoadmapUpdated");
            return Ok();
        }

        // --- ЛОГИКА ПЕРЕЗАПУСКА (RESTART) ---

        [HttpPost("{groupId}/propose-restart")]
        public async Task<IActionResult> ProposeRestart(long groupId)
        {
            var group = await _context.Groups.Include(g => g.ChatMessages).FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return NotFound();

            // Удаляем старые предложения
            var old = group.ChatMessages.Where(m => m.Content.StartsWith("[RESTART_PROPOSAL]")).ToList();
            _context.ChatMessages.RemoveRange(old);

            var user = await _context.Users.FindAsync(CurrentUserId);
            var systemMessage = new ChatMessage
            {
                GroupId = groupId,
                SenderId = CurrentUserId,
                Content = $"[RESTART_PROPOSAL]|{user.Username} предложил начать новый цикл обмена. Вы согласны?",
                SentAt = DateTime.UtcNow
            };

            _context.ChatMessages.Add(systemMessage);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("RoadmapUpdated");
            return Ok();
        }


        [HttpPost("direct/{targetUserId}")]
        public async Task<IActionResult> GetOrCreateDirectChat(long targetUserId)
        {
            var existing = await _context.Groups
                .Include(g => g.Members)
                .Where(g => g.IsSolo && g.Members.Any(m => m.UserId == CurrentUserId) && g.Members.Any(m => m.UserId == targetUserId))
                .FirstOrDefaultAsync();

            if (existing != null) return Ok(new GroupResponse { Id = existing.Id, Name = "Чат", IsSolo = true });

            var group = new Group { Name = "Совместное развитие", IsSolo = true, OwnerId = CurrentUserId, CreatedAt = DateTime.UtcNow };
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            _context.GroupMembers.AddRange(
                new GroupMember { GroupId = group.Id, UserId = CurrentUserId },
                new GroupMember { GroupId = group.Id, UserId = targetUserId }
            );
            await _context.SaveChangesAsync();

            return Ok(new GroupResponse { Id = group.Id, Name = "Чат", IsSolo = true });
        }
    }
}