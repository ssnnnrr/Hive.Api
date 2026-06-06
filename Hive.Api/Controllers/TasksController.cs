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
                t.Completions.Select(tc => new UserMinimalDto(
                    tc.User?.Username ?? "Аноним",
                    tc.User?.AvatarUrl
                )).ToList(),
                t.Comments.Select(c => new TaskCommentDto(
                    c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt
                )).ToList(),
                t.Goal?.IsSolo ?? true
            )).ToList();

            return Ok(res);
        }

        [HttpGet("my-all")]
        public async Task<IActionResult> GetAllMyTasks()
        {
            var userId = CurrentUserId;

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
                t.Comments.Select(c => new TaskCommentDto(c.Id, c.UserId, c.User?.Username ?? "Аноним", c.User?.AvatarUrl, c.Text, c.CreatedAt)).ToList(),
                t.Goal?.IsSolo ?? true
            )).ToList();

            return Ok(res);
        }

        [HttpPatch("{id}/reschedule")]
        public async Task<IActionResult> RescheduleTask(long id, [FromBody] RescheduleRequest req)
        {
            Console.WriteLine($"[BACKEND]: Rescheduling task {id} to {req.NewDate}");

            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            task.DueDate = DateTime.SpecifyKind(req.NewDate, DateTimeKind.Utc);

            _context.Tasks.Update(task);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateStatusRequest req)
        {
            var task = await _context.Tasks.Include(t => t.Goal).FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();

            var existingCompletion = await _context.TaskCompletions
                .FirstOrDefaultAsync(tc => tc.TaskId == id && tc.UserId == CurrentUserId);

            if (req.Status == "Done")
            {
                task.Status = Entities.TaskStatus.Done; // ОБЯЗАТЕЛЬНО обновляем статус задачи
                if (existingCompletion == null)
                {
                    _context.TaskCompletions.Add(new TaskCompletion
                    {
                        TaskId = id,
                        UserId = CurrentUserId,
                        CompletedAt = DateTime.UtcNow
                    });
                }
            }
            else
            {
                task.Status = Entities.TaskStatus.ToDo; // Возвращаем в ToDo
                if (existingCompletion != null) _context.TaskCompletions.Remove(existingCompletion);
            }

            await _context.SaveChangesAsync();
            await UpdateGoalProgress(task.GoalId);
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

            // Обновляем прогресс цели после добавления новой задачи
            await UpdateGoalProgress(req.GoalId);

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var task = await _context.Tasks
                .Include(t => t.Completions)
                .Include(t => t.Comments)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null || task.CreatorId != CurrentUserId) return Forbid();

            var goalId = task.GoalId;

            // Удаляем связанные completions и comments
            _context.TaskCompletions.RemoveRange(task.Completions);
            _context.TaskComments.RemoveRange(task.Comments);
            _context.Tasks.Remove(task);

            await _context.SaveChangesAsync();

            // Обновляем прогресс цели после удаления задачи
            await UpdateGoalProgress(goalId);

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

        // *** НОВЫЙ МЕТОД ДЛЯ ОБНОВЛЕНИЯ ПРОГРЕССА ЦЕЛИ ***
        private async Task UpdateGoalProgress(long goalId)
        {
            var goal = await _context.Goals
                .Include(g => g.Tasks)
                    .ThenInclude(t => t.Completions)
                .Include(g => g.Collaborations)
                .FirstOrDefaultAsync(g => g.Id == goalId);

            if (goal == null) return;

            if (!goal.Tasks.Any())
            {
                goal.Progress = 0;
                await _context.SaveChangesAsync();
                return;
            }

            // Считаем прогресс создателя цели
            var creatorTasksDone = goal.Tasks.Count(t =>
                t.Completions.Any(tc => tc.UserId == goal.UserId));

            double creatorProgress = (double)creatorTasksDone / goal.Tasks.Count * 100;

            // Если цель соло - прогресс создателя и есть общий прогресс
            if (goal.IsSolo)
            {
                goal.Progress = creatorProgress;
            }
            else
            {
                // Для групповых целей считаем средний прогресс всех подтвержденных участников
                var confirmedMemberIds = goal.Collaborations
                    .Where(c => c.IsConfirmed)
                    .Select(c => c.UserId)
                    .ToList();

                // Добавляем создателя, если его еще нет в списке
                if (!confirmedMemberIds.Contains(goal.UserId))
                {
                    confirmedMemberIds.Add(goal.UserId);
                }

                double totalProgress = 0;
                foreach (var memberId in confirmedMemberIds)
                {
                    var memberTasksDone = goal.Tasks.Count(t =>
                        t.Completions.Any(tc => tc.UserId == memberId));
                    totalProgress += (double)memberTasksDone / goal.Tasks.Count * 100;
                }

                goal.Progress = confirmedMemberIds.Count > 0
                    ? totalProgress / confirmedMemberIds.Count
                    : 0;
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"[BACKEND LOG]: Updated Goal {goalId} Progress to {goal.Progress}%");
        }
    }
}