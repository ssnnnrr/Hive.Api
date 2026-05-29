using Hive.Api.Data;
using Hive.Api.Entities;
using Hive.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class FriendsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public FriendsController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet("my-friends")]
        public async Task<IActionResult> GetFriends()
        {
            var friendships = await _context.Friendships
                .Where(f => (f.UserOneId == CurrentUserId || f.UserTwoId == CurrentUserId) && f.IsAccepted)
                .Include(f => f.UserOne).ThenInclude(u => u!.ReviewsReceived)
                .Include(f => f.UserTwo).ThenInclude(u => u!.ReviewsReceived)
                .ToListAsync();

            var friends = friendships.Select(f => {
                var u = f.UserOneId == CurrentUserId ? f.UserTwo : f.UserOne;
                double avgRating = u!.ReviewsReceived.Any() ? Math.Round(u.ReviewsReceived.Average(r => (double)r.Rating), 1) : 0;

                return new UserDto(
                    u.Id, u.Username, u.Email,
                    "None", avgRating, u.AvatarUrl
                );
            }).GroupBy(u => u.Id).Select(g => g.First()).ToList();

            return Ok(friends);
        }

        [HttpPost("decline/{requestId}")]
        public async Task<IActionResult> DeclineRequest(long requestId)
        {
            var req = await _context.ChatRequests.FindAsync(requestId);
            if (req == null || req.ReceiverId != CurrentUserId) return NotFound();

            _context.ChatRequests.Remove(req);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("pending-requests")]
        public async Task<IActionResult> GetPendingRequests()
        {
            // Ищем запросы, где получатель — текущий юзер, а статус — Ожидание
            var requests = await _context.ChatRequests
                .Where(r => r.ReceiverId == CurrentUserId && r.Status == RequestStatus.Pending)
                .Include(r => r.Sender)
                .Select(r => new {
                    Id = r.Id, // ID самого запроса для Accept/Decline
                    SenderId = r.SenderId,
                    SenderName = r.Sender.Username,
                    AvatarUrl = r.Sender.AvatarUrl
                })
                .ToListAsync();

            return Ok(requests);
        }

        [HttpPost("request/{targetId}")]
        public async Task<IActionResult> SendRequest(long targetId)
        {
            if (targetId == CurrentUserId) return BadRequest("Нельзя отправить запрос самому себе");

            // Проверяем, нет ли уже такого запроса (в любую сторону)
            var exists = await _context.ChatRequests.AnyAsync(r =>
                (r.SenderId == CurrentUserId && r.ReceiverId == targetId) ||
                (r.SenderId == targetId && r.ReceiverId == CurrentUserId));

            if (exists) return BadRequest("Запрос уже существует или вы уже партнеры");

            var request = new ChatRequest
            {
                SenderId = CurrentUserId,
                ReceiverId = targetId,
                Status = RequestStatus.Pending
            };

            _context.ChatRequests.Add(request);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{friendId}")]
        public async Task<IActionResult> DeleteFriend(long friendId)
        {
            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f => (f.UserOneId == CurrentUserId && f.UserTwoId == friendId) ||
                                          (f.UserTwoId == CurrentUserId && f.UserOneId == friendId));

            if (friendship == null) return NotFound();

            _context.Friendships.Remove(friendship);

            // Удаляем также запрос на чат, чтобы обнулить статус отношений
            var request = await _context.ChatRequests
                .FirstOrDefaultAsync(r => (r.SenderId == CurrentUserId && r.ReceiverId == friendId) ||
                                          (r.SenderId == friendId && r.ReceiverId == CurrentUserId));
            if (request != null) _context.ChatRequests.Remove(request);

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("accept/{requestId}")]
        public async Task<IActionResult> AcceptRequest(long requestId)
        {
            var request = await _context.ChatRequests.Include(r => r.Sender).Include(r => r.Receiver).FirstOrDefaultAsync(r => r.Id == requestId);
            if (request == null || request.ReceiverId != CurrentUserId) return NotFound();

            request.Status = RequestStatus.Accepted;

            var friendshipExists = await _context.Friendships.AnyAsync(f =>
                (f.UserOneId == request.SenderId && f.UserTwoId == request.ReceiverId) ||
                (f.UserOneId == request.ReceiverId && f.UserTwoId == request.SenderId));

            if (!friendshipExists)
            {
                _context.Friendships.Add(new Friendship { UserOneId = request.SenderId, UserTwoId = request.ReceiverId, IsAccepted = true });
            }

            // Создаем чат только если его нет
            var chatExists = await _context.Groups.AnyAsync(g => g.IsSolo && g.Members.Any(m => m.UserId == request.SenderId) && g.Members.Any(m => m.UserId == request.ReceiverId));
            if (!chatExists)
            {
                var group = new Group { Name = "Совместное развитие", IsSolo = true, OwnerId = request.SenderId, CreatedAt = DateTime.UtcNow };
                _context.Groups.Add(group);
                await _context.SaveChangesAsync();
                _context.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = request.SenderId });
                _context.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = request.ReceiverId });
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}