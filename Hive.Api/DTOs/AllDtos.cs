using Hive.Api.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Hive.Api.DTOs
{
    public record LoginRequest(string Email, string Password);
    public record RegisterRequest(string Username, string Email, string Password);
    public record AuthResponse(string Token, UserDto User);
    public record UserDto(
        long Id,
        string Username,
        string Email,
        string SynergyLevel,
        string? AvatarUrl,
        List<string>? MatchTeaching = null,
        List<string>? MatchLearning = null
    );
    public record UserProfileDto(long Id, string Username, string Email, List<UserSkillDto> Skills, List<ReviewDto> Reviews, double Rating, string RelationshipStatus, string? AvatarUrl);
    public record UserSkillDto(long SkillId, string skillName, string Type);
    public record SkillDto(long Id, string Name);
    public record SyncSkillsRequest(List<long> SkillIds, string Type);
    public record UpdateProfileRequest(string Username, string? NewPassword, string? ConfirmPassword, bool IsPrivate, string? AvatarUrl);

    public record ReviewDto(long Id, short Rating, string? Comment, string ReviewerName, DateTime CreatedAt);
    public record ReviewRequest(long ReviewedId, short Rating, string? Comment);

    public record GoalResponse(
        long Id, string Title, string? Description, string? MeasurableResult,
        double Progress, DateTime TargetDate, bool IsSolo, string GoalType,
        List<TaskResponse> Tasks, List<GoalPartnerDto> Collaborators,
        List<MaterialDto> Materials, // Используем переименованный DTO
        long UserId
    );

    public record UserMinimalDto(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("avatarUrl")] string? AvatarUrl
    );


    public class UploadMaterialRequest
    {
        public int GoalId { get; set; }
        public string Title { get; set; }
        public string? Content { get; set; } // Для ссылок
        public IFormFile? File { get; set; } // Для файлов
        public int? TaskId { get; set; }
    }

    public record TaskResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("dueDate")] DateTime DueDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("goalId")] long GoalId,
    [property: JsonPropertyName("goalTitle")] string GoalTitle,
    [property: JsonPropertyName("creatorId")] long CreatorId,
    [property: JsonPropertyName("assigneeId")] long? AssigneeId,
    [property: JsonPropertyName("artifactUrl")] string? ArtifactUrl,
    [property: JsonPropertyName("studentComment")] string? StudentComment,
    [property: JsonPropertyName("teacherComment")] string? TeacherComment,
    // 12-й:
    [property: JsonPropertyName("completions")] List<UserMinimalDto> Completions,
    // 13-й:
    [property: JsonPropertyName("comments")] List<TaskCommentDto> Comments,
    // 14-й:
    [property: JsonPropertyName("isSolo")] bool IsSolo
);

    public record RescheduleRequest(
        [property: JsonPropertyName("newDate")] DateTime NewDate
    );

    public record TaskCommentDto(long Id, long UserId, string UserName, string? UserAvatar, string Text, DateTime CreatedAt); public record AddCommentRequest(long TaskId, string Text);
    public class MaterialDto
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; }
        public long CreatorId { get; set; }
        public string CreatorName { get; set; }
        public string? CreatorAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? TaskId { get; set; }
        public string? TaskTitle { get; set; }

        // Пустой конструктор
        public MaterialDto() { }

        // Конструктор со всеми параметрами
        public MaterialDto(
            long id,
            string title,
            string content,
            string type,
            long creatorId,
            string creatorName,
            string? creatorAvatarUrl,
            DateTime createdAt,
            int? taskId = null,
            string? taskTitle = null)
        {
            Id = id;
            Title = title;
            Content = content;
            Type = type;
            CreatorId = creatorId;
            CreatorName = creatorName;
            CreatorAvatarUrl = creatorAvatarUrl;
            CreatedAt = createdAt;
            TaskId = taskId;
            TaskTitle = taskTitle;
        }
    }

    // При приглашении друга
    public record PartnerDto(long Id, string Username, string? AvatarUrl, bool IsConfirmed);

    // Для создания шага
    public record AddRoadmapStepRequest(
    long GroupId,
    string Content,
    DateTime DueDate,
    string? InstructionUrl,
    bool IsTest = false,
    bool IsRequired = true,
    int MaxAttempts = 3 // Это поле учитель заполнит в интерфейсе
);
    // Для обновления статуса учеником (сдача работы)
    public record SubmitRoadmapStepRequest(
        long StepId,
        string ArtifactUrl // Ссылка на результат
    );

    // Для проверки учителем
    public record ReviewRoadmapStepRequest(
        long StepId,
        bool Approve,
        string? Comment
    );
    public record UpdateRoadmapStepRequest(
    string? Content,
    DateTime DueDate,
    string? InstructionUrl,
    bool? IsRequired
);
    public record UpdateRoadmapStatusRequest(string Role, bool IsDone, string? Comment);

    public record SubmitTaskRequest(long TaskId, string? ArtifactUrl, string? ResultComment);
    public record VerifyTaskRequest(long TaskId, bool Approve);
    public record AddMaterialRequest(long GoalId, string Title, string Content, long? TaskId, string Type);
    public record GoalPartnerDto(long Id, string Name, double Progress, string? AvatarUrl, bool IsConfirmed, bool IsAdmin);
    public record CreateGoalRequest(string Title, string Description, string MeasurableResult, DateTime TargetDate, bool IsSolo, string GoalType, List<TaskDraftResponse> Steps);
    public record SmartGoalRequest(string Title, string Why, string MeasurableResult, DateTime TargetDate);
    public record TaskDraftResponse(string Title, DateTime DueDate);
    public record CreateTaskRequest(
        long GoalId,
        string Title,
        DateTime DueDate,
        long? AssigneeId,
        bool IsArtifactRequired // <--- Добавили
    );

    public record UpdateTaskRequest(string Title);
    // Добавляем поле Comment, чтобы ученик мог писать пояснения
 

public class UpdateStatusRequest
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }
    }
    public record CreateEventRequest(
        string Title,
        string? Description,
        DateTime EventDate,
        long? GroupId,
        string? LinkUrl,
        string? Location,
        string? ImageUrl // Добавлено
    );

    public record SubmitStepRequest(long StepId, string ArtifactUrl, string? StudentComment);

    public record VerifyStepRequest(long StepId, bool Approve, string? Comment);


    public record PinMessageRequest(long MessageId, bool IsPinned);

    // Комментарии к шагам обучения
    public record StepCommentDto(long Id, long StepId, long UserId, string UserName, string? UserAvatar, string Text, DateTime CreatedAt);
    public record AddStepCommentRequest(long StepId, string Text);
    public record UpdateStepCommentRequest(string Text);

    // Конструктор тестов
    public record GenerateTestRequest(string Topic, string Format, int QuestionsCount);
    public record SubmitTestResultRequest(long StepId, double Score);

    // Структура одного вопроса теста (для парсинга TestData)
    public class TestQuestion
    {
        public string Question { get; set; } = "";
        public List<string> Options { get; set; } = new();
        public string CorrectAnswer { get; set; } = "";
    }


    public record EventResponse(
        long Id,
        string Title,
        string? Description,
        DateTime EventDate,
        bool IsCompleted,
        long? GroupId,
        string CreatorName,
        string? LinkUrl,
        string? Location,
        string? ImageUrl // Добавлено
    );

    public class GroupResponse
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string OwnerName { get; set; }
        public int MembersCount { get; set; }
        public bool IsSolo { get; set; }
        public long? OtherUserId { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public int UnreadCount { get; set; }
        public string? LastMessage { get; set; } // Добавляем поле
    }


    public class GroupDetailResponse
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public long OwnerId { get; set; }
        public bool IsSolo { get; set; }
        public List<UserBriefDto> Members { get; set; }
    }


        public class SubmitStepRequestDto
        {
            public long StepId { get; set; }
            public string? StudentComment { get; set; }
            public string? File { get; set; }        // base64 строка
            public string? FileName { get; set; }    // оригинальное имя файла
            public string? ContentType { get; set; } // MIME тип
        }


    // Было: public record SubmitTestAttemptRequest(long StepId, double Score);
    // СТАЛО:
    public record SubmitTestAttemptRequest(long StepId, double Score, string? AnswersJson);

    public class UserBriefDto
    {
        public long Id { get; set; }
        public string Username { get; set; }
        public string? AvatarUrl { get; set; }
    }
    public record MessageDto(long Id, string Content, long SenderId, string SenderName, DateTime SentAt, bool IsRead); public record SendMessageRequest(long GroupId, string Content);
}