using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Hive.Api.Hubs;
using Hive.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.Json;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly HiveDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly AIService _aiService;

        public ChatController(HiveDbContext context, IHubContext<ChatHub> hubContext, AIService aiService)
        {
            _context = context;
            _hubContext = hubContext;
            _aiService = aiService;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // --- СООБЩЕНИЯ (MESSAGES) ---

        [HttpGet("{groupId}/messages")]
        public async Task<IActionResult> GetMessages(long groupId)
        {
            // Проверяем, что пользователь состоит в группе
            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == CurrentUserId);

            if (!isMember)
                return Forbid();

            var messages = await _context.ChatMessages
                .Where(m => m.GroupId == groupId && !m.IsDeleted)
                .Include(m => m.Sender)
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageDto(
                    m.Id,
                    m.Content,
                    m.SenderId,
                    m.Sender!.Username,
                    m.SentAt
                ))
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest req)
        {
            // Проверяем членство в группе
            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == req.GroupId && gm.UserId == CurrentUserId);

            if (!isMember)
                return Forbid();

            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return Unauthorized();

            var message = new ChatMessage
            {
                Content = req.Content,
                GroupId = req.GroupId,
                SenderId = CurrentUserId,
                SentAt = DateTime.UtcNow,
                IsPinned = false,
                IsDeleted = false
            };

            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();

            var dto = new MessageDto(
                message.Id,
                message.Content,
                message.SenderId,
                user.Username,
                message.SentAt
            );

            await _hubContext.Clients.Group(req.GroupId.ToString())
                .SendAsync("ReceiveMessage", dto);

            return Ok(dto);
        }

        [HttpDelete("messages/{messageId}")]
        public async Task<IActionResult> DeleteMessage(long messageId)
        {
            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null) return NotFound();
            if (msg.SenderId != CurrentUserId) return Forbid();

            msg.IsDeleted = true; // Мягкое удаление
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(msg.GroupId.ToString())
                .SendAsync("MessageDeleted", new { messageId });

            return Ok();
        }

        [HttpPost("roadmap/toggle-complete")]
        public async Task<IActionResult> ToggleComplete([FromBody] JsonElement body)
        {
            // Извлекаем id из JSON объекта
            if (!body.TryGetProperty("stepId", out var idProp))
                return BadRequest("Укажите stepId");

            long stepId = idProp.GetInt64();

            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();

            // Простая логика: если было Done -> ставим ToDo, иначе -> Done
            if (step.Status == Entities.TaskStatus.Done)
                step.Status = Entities.TaskStatus.ToDo;
            else
                step.Status = Entities.TaskStatus.Done;

            await _context.SaveChangesAsync();
            return Ok(new { step.Id, Status = step.Status.ToString() });
        }


        [HttpPost("messages/{messageId}/pin")]
        public async Task<IActionResult> TogglePin(long messageId, [FromQuery] bool pin)
        {
            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null) return NotFound();

            msg.IsPinned = pin;
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(msg.GroupId.ToString())
                .SendAsync("MessagePinned", new { messageId, isPinned = pin });

            return Ok();
        }

        // --- ПЛАН ОБУЧЕНИЯ (ROADMAP) ---

        [HttpGet("{groupId}/roadmap")]
        public async Task<IActionResult> GetRoadmap(long groupId)
        {
            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == CurrentUserId);

            if (!isMember) return Forbid();

            var stepsFromDb = await _context.RoadmapSteps
                .Where(s => s.GroupId == groupId)
                .Include(s => s.StepComments)
                    .ThenInclude(c => c.User)
                .OrderBy(s => s.DueDate)
                .ToListAsync();

            var result = stepsFromDb.Select(s => new {
                s.Id,
                s.Content,
                s.DueDate,
                Status = s.Status.ToString(),
                s.CreatorId,
                s.InstructionUrl,
                s.ArtifactUrl,
                s.TeacherComment,
                s.StudentComment,
                s.IsTest,
                s.TestData,
                s.TestScore,
                s.IsRequired,
                s.GroupId,

                // --- ВОТ ЭТИ ДВЕ СТРОЧКИ НУЖНО ДОБАВИТЬ! ---
                s.MaxAttempts,
                s.UsedAttempts,
                // ------------------------------------------

                Comments = s.StepComments?
                    .OrderBy(c => c.CreatedAt)
                    .Select(c => new StepCommentDto(
                        c.Id,
                        c.RoadmapStepId,
                        c.UserId,
                        c.User?.Username ?? "Удален",
                        c.User?.AvatarUrl,
                        c.Text,
                        c.CreatedAt
                    )).ToList() ?? new List<StepCommentDto>()
            });

            return Ok(result);
        }

        [HttpPost("roadmap")]
        public async Task<IActionResult> AddRoadmapStep([FromBody] AddRoadmapStepRequest req)
        {
            if (req.DueDate == default)
                return BadRequest("Дедлайн обязателен");

            var step = new RoadmapStep
            {
                GroupId = req.GroupId,
                Content = req.Content,
                CreatorId = CurrentUserId,
                DueDate = DateTime.SpecifyKind(req.DueDate, DateTimeKind.Utc),
                InstructionUrl = req.InstructionUrl,
                Status = Entities.TaskStatus.ToDo,
                IsTest = req.IsTest,
                IsRequired = req.IsRequired,
                MaxAttempts = req.MaxAttempts // ИСПРАВЛЕНО: просто присваиваем значение из DTO
            };

            _context.RoadmapSteps.Add(step);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                step.Id,
                step.Content,
                step.DueDate,
                Status = step.Status.ToString(),
                step.CreatorId,
                step.IsTest,
                step.IsRequired,
                step.MaxAttempts
            });
        }

        [HttpGet("download/{fileName}")]
        [AllowAnonymous] // Если нужно разрешить доступ без авторизации
        public IActionResult DownloadFile(string fileName)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("Файл не найден");

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var contentType = GetContentType(fileName);

            return File(fileBytes, contentType, fileName);
        }

        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }

        [HttpPost("roadmap/submit")]
        public async Task<IActionResult> SubmitStep([FromBody] JsonElement req)
        {
            // Извлекаем данные из JSON
            long stepId = req.GetProperty("stepId").GetInt64();
            string? studentComment = req.TryGetProperty("studentComment", out var comment)
                ? comment.GetString() : null;
            string? file = req.TryGetProperty("file", out var fileElem)
                ? fileElem.GetString() : null;
            string? fileName = req.TryGetProperty("fileName", out var nameElem)
                ? nameElem.GetString() : "file";

            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();

            if (step.CreatorId == CurrentUserId)
                return BadRequest("Вы создали этот шаг, вы не можете его сдавать");

            // Обработка файла
            if (!string.IsNullOrEmpty(file))
            {
                try
                {
                    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsPath))
                        Directory.CreateDirectory(uploadsPath);

                    var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
                    var filePath = Path.Combine(uploadsPath, uniqueFileName);

                    var fileBytes = Convert.FromBase64String(file);
                    await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                    step.ArtifactUrl = uniqueFileName;
                }
                catch (Exception ex)
                {
                    return BadRequest($"Ошибка сохранения файла: {ex.Message}");
                }
            }

            step.Status = Entities.TaskStatus.UnderReview;
            step.StudentComment = studentComment;

            _context.Notifications.Add(new Notification
            {
                UserId = step.CreatorId,
                Title = "Задание на проверку",
                Message = $"Партнер сдал работу: {step.Content}",
                Type = "TaskReview",
                CreatedAt = DateTime.UtcNow,
                RoadmapStepId = step.Id
            });

            await _context.SaveChangesAsync();
            return Ok(new { step.Id, Status = step.Status.ToString() });
        }

        [HttpPost("roadmap/verify")]
        public async Task<IActionResult> VerifyStep([FromBody] VerifyStepRequest req)
        {
            var step = await _context.RoadmapSteps.FindAsync(req.StepId);
            if (step == null) return NotFound();
            if (step.CreatorId != CurrentUserId) return Forbid();

            if (req.Approve)
            {
                step.Status = Entities.TaskStatus.Done;
                step.TeacherComment = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.Comment))
                    return BadRequest("Необходимо указать замечания");
                step.Status = Entities.TaskStatus.ToDo;
                step.TeacherComment = req.Comment;
            }

            await _context.SaveChangesAsync();
            return Ok(new { step.Id, Status = step.Status.ToString() });
        }

        [HttpPost("roadmap/comment")]
        public async Task<IActionResult> AddStepComment([FromBody] AddStepCommentRequest req)
        {
            var comment = new RoadmapStepComment
            {
                RoadmapStepId = req.StepId,
                UserId = CurrentUserId,
                Text = req.Text,
                CreatedAt = DateTime.UtcNow
            };

            _context.StepComments.Add(comment);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(CurrentUserId);
            return Ok(new StepCommentDto(
                comment.Id,
                comment.RoadmapStepId,
                comment.UserId,
                user!.Username,
                user.AvatarUrl,
                comment.Text,
                comment.CreatedAt
            ));
        }

        [HttpDelete("roadmap/comment/{id}")]
        public async Task<IActionResult> DeleteStepComment(long id)
        {
            var comment = await _context.StepComments.FindAsync(id);
            if (comment == null) return NotFound();
            if (comment.UserId != CurrentUserId) return Forbid();

            _context.StepComments.Remove(comment);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("roadmap/generate-test")]
        public async Task<IActionResult> GenerateTestWithAI([FromBody] GenerateTestRequest req)
        {
            var testJson = await _aiService.GenerateTestJsonAsync(req.Topic, req.Format, req.QuestionsCount);
            if (string.IsNullOrEmpty(testJson))
                return BadRequest("GigaChat не смог сгенерировать тест");
            return Ok(new { testData = testJson });
        }

        [HttpPost("roadmap/{stepId}/save-test")]
        public async Task<IActionResult> SaveTestToStep(long stepId, [FromBody] JsonElement testJson)
        {
            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null || step.CreatorId != CurrentUserId)
                return Forbid();

            step.IsTest = true;
            step.TestData = testJson.GetRawText(); // Сохраняем как строку JSON
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("roadmap/{stepId}")]
        public async Task<IActionResult> UpdateRoadmapStep(long stepId, [FromBody] UpdateRoadmapStepRequest req)
        {
            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();
            if (step.CreatorId != CurrentUserId) return Forbid();

            if (req.Content != null) step.Content = req.Content;
            if (req.DueDate != default) step.DueDate = DateTime.SpecifyKind(req.DueDate, DateTimeKind.Utc);
            if (req.InstructionUrl != null) step.InstructionUrl = req.InstructionUrl;
            if (req.IsRequired.HasValue) step.IsRequired = req.IsRequired.Value;

            await _context.SaveChangesAsync();
            return Ok();
        }


        [HttpPut("roadmap/{stepId}/test")]
        public async Task<IActionResult> UpdateStepTest(long stepId, [FromBody] JsonElement testJson)
        {
            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();
            if (step.CreatorId != CurrentUserId) return Forbid();

            step.TestData = testJson.GetRawText();
            await _context.SaveChangesAsync();
            return Ok();
        }



        [HttpPost("roadmap/submit-test")]
        public async Task<IActionResult> SubmitTestResults([FromBody] SubmitTestResultRequest req)
        {
            var step = await _context.RoadmapSteps.FindAsync(req.StepId);
            if (step == null) return NotFound();

            step.TestScore = req.Score;
            if (req.Score >= 0.8)
                step.Status = Entities.TaskStatus.Done;

            await _context.SaveChangesAsync();
            return Ok(new { score = req.Score, status = step.Status.ToString() });
        }

        // ПОЛНЫЙ ИСПРАВЛЕННЫЙ МЕТОД GetAllMyRoadmaps
        [HttpGet("roadmap/all-my")]
        public async Task<IActionResult> GetAllMyRoadmaps()
        {
            var userGroupIds = await _context.GroupMembers
                .Where(gm => gm.UserId == CurrentUserId)
                .Select(gm => gm.GroupId)
                .ToListAsync();

            if (!userGroupIds.Any()) return Ok(new List<object>());

            // 1. Сначала загружаем данные из БД со связями
            var stepsFromDb = await _context.RoadmapSteps
                .Where(s => userGroupIds.Contains(s.GroupId))
                .Include(s => s.StepComments)
                    .ThenInclude(c => c.User)
                .OrderBy(s => s.DueDate)
                .ToListAsync();

            // 2. Преобразуем в анонимные объекты или DTO
            var result = stepsFromDb.Select(s => new {
                s.Id,
                s.Content,
                s.DueDate,
                Status = s.Status.ToString(),
                s.CreatorId,
                CreatorName = _context.Users.FirstOrDefault(u => u.Id == s.CreatorId)?.Username ?? "Мастер",
                s.InstructionUrl,
                s.ArtifactUrl,
                s.TeacherComment,
                s.StudentComment,
                s.IsTest,
                s.TestData,
                s.TestScore,
                s.IsRequired,
                s.GroupId,
                s.MaxAttempts,
                s.UsedAttempts,
                // ИСПРАВЛЕНИЕ ТУТ: Мапим в конкретный StepCommentDto
                Comments = s.StepComments?
        .Select(c => new StepCommentDto(
            c.Id,
            c.RoadmapStepId,
            c.UserId,
            c.User?.Username ?? "Аноним",
            c.User?.AvatarUrl,
            c.Text,
            c.CreatedAt
        )).ToList() ?? new List<StepCommentDto>() // Теперь типы совпадают (List<StepCommentDto>)
            });

            return Ok(result);
        }


        [HttpPost("roadmap/submit-test-attempt")]
        public async Task<IActionResult> SubmitTestAttempt([FromBody] SubmitTestAttemptRequest request)
        {
            var step = await _context.RoadmapSteps.FindAsync(request.StepId);
            if (step == null) return NotFound();

            // ВАЖНО: Увеличиваем счетчик
            step.UsedAttempts++;

            // Сохраняем результат
            step.TestScore = request.Score;
            step.StudentComment = request.AnswersJson;

            // Если тест пройден хорошо или попытки кончились - меняем статус
            if (request.Score >= 0.8 || step.UsedAttempts >= step.MaxAttempts)
            {
                step.Status = Entities.TaskStatus.Done;
            }

            await _context.SaveChangesAsync();
            return Ok(step);
        }

        [HttpPost("upload-file")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Файл не выбран");

            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);

            // Генерируем уникальное имя файла
            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Возвращаем только имя файла (или путь относительно wwwroot)
            return Ok(new { fileName = uniqueFileName });
        }

        // 2. Метод «Сохранить результат» (Ручная фиксация учеником)
        [HttpPost("roadmap/finalize-test")]
        public async Task<IActionResult> FinalizeTest([FromBody] JsonElement body)
        {
            // Извлекаем stepId из JSON объекта
            if (!body.TryGetProperty("stepId", out var stepIdProp))
                return BadRequest("Укажите stepId");

            long stepId = stepIdProp.GetInt64();

            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();

            if (step.Status == Hive.Api.Entities.TaskStatus.Done)
                return BadRequest("Уже сохранено.");

            // Фиксируем результат
            step.Status = Hive.Api.Entities.TaskStatus.Done;

            await _context.SaveChangesAsync();
            return Ok(new { Status = step.Status.ToString() });
        }


        [HttpDelete("roadmap/{stepId}")]
        public async Task<IActionResult> DeleteStep(long stepId)
        {
            var step = await _context.RoadmapSteps.FindAsync(stepId);
            if (step == null) return NotFound();
            if (step.CreatorId != CurrentUserId) return Forbid();

            _context.RoadmapSteps.Remove(step);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}