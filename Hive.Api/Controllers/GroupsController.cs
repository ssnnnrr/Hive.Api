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
    public class GroupsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public GroupsController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // GET: api/groups - получить все группы текущего пользователя
        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var groups = await _context.Groups
                .Include(g => g.Members)
                .ThenInclude(m => m.User)
                .Include(g => g.Owner)
                .Include(g => g.ChatMessages)
                .Where(g => g.Members.Any(m => m.UserId == CurrentUserId))
                .OrderByDescending(g => g.ChatMessages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => m.SentAt)
                    .FirstOrDefault())
                .Select(g => new GroupResponse
                {
                    Id = g.Id,
                    Name = g.IsSolo
                        ? g.Members.Where(m => m.UserId != CurrentUserId)
                            .Select(m => m.User.Username)
                            .FirstOrDefault() ?? "Чат"
                        : g.Name,
                    Description = g.Description,
                    OwnerName = g.Owner.Username,
                    MembersCount = g.Members.Count,
                    IsSolo = g.IsSolo,
                    OtherUserId = g.IsSolo
                        ? g.Members.Where(m => m.UserId != CurrentUserId)
                            .Select(m => m.UserId)
                            .FirstOrDefault()
                        : null,
                    LastMessage = g.ChatMessages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Content)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(groups);
        }

        // GET: api/groups/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserGroups(long userId)
        {
            var groups = await _context.GroupMembers
                .Where(gm => gm.UserId == CurrentUserId)
                .Include(gm => gm.Group)
                .ThenInclude(g => g!.Members)
                .ThenInclude(m => m.User)
                .Include(gm => gm.Group)
                .ThenInclude(g => g!.Owner)
                .Select(gm => gm.Group)
                .ToListAsync();

            var res = groups.Select(g => {
                var otherMember = g!.IsSolo ? g.Members.FirstOrDefault(m => m.UserId != CurrentUserId)?.User : null;
                return new GroupResponse
                {
                    Id = g.Id,
                    Name = g.IsSolo ? (otherMember?.Username ?? "Приватный чат") : g.Name,
                    Description = g.Description ?? "",
                    OwnerName = g.Owner?.Username ?? "Система",
                    MembersCount = g.Members.Count,
                    IsSolo = g.IsSolo,
                    OtherUserId = otherMember?.Id
                };
            }).ToList();

            return Ok(res);
        }

        // POST: api/groups/direct/{targetUserId}
        [HttpPost("direct/{targetUserId}")]
        public async Task<IActionResult> GetOrCreateDirectChat(long targetUserId)
        {
            // Проверяем существование пользователей
            var currentUser = await _context.Users.FindAsync(CurrentUserId);
            var targetUser = await _context.Users.FindAsync(targetUserId);

            if (currentUser == null || targetUser == null)
                return NotFound("User not found");

            // Ищем существующий чат
            var existing = await _context.Groups
                .Where(g => g.IsSolo
                    && g.Members.Any(m => m.UserId == CurrentUserId)
                    && g.Members.Any(m => m.UserId == targetUserId))
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return Ok(new GroupResponse
                {
                    Id = existing.Id,
                    Name = targetUser.Username,
                    OwnerName = targetUser.Username,
                    MembersCount = 2,
                    IsSolo = true,
                    OtherUserId = targetUserId
                });
            }

            // Создаем новый чат
            var group = new Group
            {
                Name = $"{currentUser.Username} & {targetUser.Username}",
                IsSolo = true,
                OwnerId = CurrentUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            _context.GroupMembers.AddRange(
                new GroupMember { GroupId = group.Id, UserId = CurrentUserId },
                new GroupMember { GroupId = group.Id, UserId = targetUserId }
            );
            await _context.SaveChangesAsync();

            return Ok(new GroupResponse
            {
                Id = group.Id,
                Name = targetUser.Username,
                OwnerName = targetUser.Username,
                MembersCount = 2,
                IsSolo = true,
                OtherUserId = targetUserId
            });
        }

        // GET: api/groups/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetGroup(long id)
        {
            var group = await _context.Groups
                .Include(g => g.Members)
                .ThenInclude(m => m.User)
                .Include(g => g.Owner)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
                return NotFound();

            if (!group.Members.Any(m => m.UserId == CurrentUserId))
                return Forbid();

            return Ok(new GroupDetailResponse
            {
                Id = group.Id,
                Name = group.Name,
                OwnerId = group.OwnerId,
                IsSolo = group.IsSolo,
                Members = group.Members.Select(m => new UserBriefDto
                {
                    Id = m.UserId,
                    Username = m.User.Username,
                    AvatarUrl = m.User.AvatarUrl
                }).ToList()
            });
        }
    }
}