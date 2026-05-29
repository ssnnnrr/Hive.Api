using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class TasksController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public TasksController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpGet("goal/{goalId}")]
        public async Task<IActionResult> GetByGoal(long goalId)
        {
            var tasks = await _context.Tasks
                .Include(t => t.Goal)
                .Include(t => t.Comments).ThenInclude(c => c.User)
                .Include(t => t.Completions).ThenInclude(tc => tc.User)
                .Where(t => t.GoalId == goalId)
                .OrderBy(t => t.DueDate)
                .ToListAsync();

            var res = tasks.Select(t => new TaskResponse(
                t.Id,
                t.Title,
                t.DueDate,
                t.Status.ToString(),
                t.GoalId,
                t.Goal?.Title ?? "",
                t.CreatorId,
                t.AssigneeId,
                t.ArtifactUrl,
                t.StudentComment,
                t.TeacherComment,
                // Собираем Username и AvatarUrl
                t.Completions.Select(tc => new UserMinimalDto(
                    tc.User?.Username ?? "Аноним",
                    tc.User?.AvatarUrl
                )).ToList(),
                t.Comments.Select(c => new TaskCommentDto(
                    c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt
                )).ToList()
            )).ToList();

            return Ok(res);
        }

        [HttpGet("my-all")]
        public async Task<IActionResult> GetAllMyTasks()
        {
            var userId = CurrentUserId;

            // Ищем все цели, где пользователь - владелец или подтвержденный партнер
            var goalIds = await _context.GoalCollaborations
                .Where(c => c.UserId == userId && (c.IsConfirmed || _context.Goals.Any(g => g.Id == c.GoalId && g.UserId == userId)))
                .Select(c => c.GoalId)
                .ToListAsync();

            var tasks = await _context.Tasks
                .Include(t => t.Goal)
                .Include(t => t.Completions).ThenInclude(tc => tc.User)
                .Include(t => t.Comments).ThenInclude(c => c.User)
                .Where(t => goalIds.Contains(t.GoalId))
                .OrderBy(t => t.DueDate)
                .ToListAsync();

            var res = tasks.Select(t => new TaskResponse(
                t.Id,
                t.Title,
                t.DueDate,
                t.Status.ToString(),
                t.GoalId,
                t.Goal?.Title ?? "",
                t.CreatorId,
                t.AssigneeId,
                t.ArtifactUrl,
                t.StudentComment,
                t.TeacherComment,
                t.Completions.Select(tc => new UserMinimalDto(tc.User?.Username ?? "Аноним", tc.User?.AvatarUrl)).ToList(),
                t.Comments.Select(c => new TaskCommentDto(c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt)).ToList()
            )).ToList();

            return Ok(res);
        }

        [HttpPatch("{id}/reschedule")]
        public async Task<IActionResult> RescheduleTask(long id, [FromBody] RescheduleRequest req)
        {
            // Логируем для отладки в консоль Visual Studio
            Console.WriteLine($"[BACKEND]: Rescheduling task {id} to {req.NewDate}");

            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            // PostgreSQL требует UTC. Принудительно задаем Kind, если он не пришел.
            task.DueDate = DateTime.SpecifyKind(req.NewDate, DateTimeKind.Utc);

            _context.Tasks.Update(task);
            await _context.SaveChangesAsync();

            return Ok();
        }



        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateStatusRequest req)
        {
            Console.WriteLine($"[BACKEND LOG]: Updating task {id} to {req.Status}");

            var task = await _context.Tasks
                .Include(t => t.Goal)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound();

            var existingCompletion = await _context.TaskCompletions
                .FirstOrDefaultAsync(tc => tc.TaskId == id && tc.UserId == CurrentUserId);

            if (req.Status == "Done")
            {
                if (existingCompletion == null)
                {
                    _context.TaskCompletions.Add(new TaskCompletion
                    {
                        TaskId = id,
                        UserId = CurrentUserId,
                        CompletedAt = DateTime.UtcNow
                    });
                    Console.WriteLine($"[BACKEND LOG]: Saved Done status for Task {id}, User {CurrentUserId}");
                }
            }
            else
            {
                if (existingCompletion != null)
                {
                    _context.TaskCompletions.Remove(existingCompletion);
                    Console.WriteLine($"[BACKEND LOG]: Removed Done status (set to ToDo) for Task {id}");
                }
            }

            await _context.SaveChangesAsync();

            // Пересчет прогресса цели для текущего пользователя
            if (task.Goal != null)
            {
                var total = await _context.Tasks.CountAsync(t => t.GoalId == task.GoalId);
                var done = await _context.TaskCompletions
                    .CountAsync(tc => tc.UserId == CurrentUserId &&
                                     _context.Tasks.Any(t => t.Id == tc.TaskId && t.GoalId == task.GoalId));

                task.Goal.Progress = total > 0 ? (double)done / total * 100 : 0;
                await _context.SaveChangesAsync();
                Console.WriteLine($"[BACKEND LOG]: New Goal {task.GoalId} Progress: {task.Goal.Progress}%");
            }

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest req)
        {
            var task = new HiveTask
            {
                GoalId = req.GoalId,
                Title = req.Title,
                DueDate = DateTime.SpecifyKind(req.DueDate, DateTimeKind.Utc),
                CreatorId = CurrentUserId
            };
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null || task.CreatorId != CurrentUserId) return Forbid();
            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("comments")]
        public async Task<IActionResult> AddComment([FromBody] AddCommentRequest req)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            var comment = new TaskComment { TaskId = req.TaskId, UserId = CurrentUserId, Text = req.Text };
            _context.TaskComments.Add(comment);
            await _context.SaveChangesAsync();
            return Ok(new TaskCommentDto(comment.Id, comment.UserId, user.Username, user.AvatarUrl, comment.Text, comment.CreatedAt));
        }

        [HttpDelete("comments/{id}")]
        public async Task<IActionResult> DeleteComment(long id)
        {
            var comment = await _context.TaskComments.FindAsync(id);
            if (comment == null) return NotFound();
            _context.TaskComments.Remove(comment);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateTaskRequest req)
        {
            var t = await _context.Tasks.FindAsync(id);
            if (t == null) return NotFound();
            t.Title = req.Title;
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}