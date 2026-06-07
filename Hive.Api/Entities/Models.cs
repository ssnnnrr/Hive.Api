using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Hive.Api.Entities
{
    public enum TaskStatus { ToDo, UnderReview, Done }
    public enum RequestStatus { Pending, Accepted }
    public enum SkillType { Teaching, Learning }
    public enum GoalType { Social, Exchange, Group }
    public enum MaterialType { Link, File }
    public enum TaskPriority { Low, Medium, High }
    public enum UserRole { Admin, Member }

    public class User
    {
        [Key] public long Id { get; set; }
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<UserSkill> UserSkills { get; set; } = new List<UserSkill>();
        public virtual ICollection<Review> ReviewsReceived { get; set; } = new List<Review>();
        public virtual ICollection<GoalCollaboration> GoalCollaborations { get; set; } = new List<GoalCollaboration>();
    }

    public class Goal
    {
        [Key] public long Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? MeasurableResult { get; set; }
        public DateTime TargetDate { get; set; }
        public bool IsSolo { get; set; } = true;
        public long UserId { get; set; }
        [ForeignKey("UserId")] public virtual User? User { get; set; }
        public GoalType Type { get; set; } = GoalType.Social;
        public double Progress { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<HiveTask> Tasks { get; set; } = new List<HiveTask>();
        public virtual ICollection<GoalCollaboration> Collaborations { get; set; } = new List<GoalCollaboration>();
        public virtual ICollection<Material> Materials { get; set; } = new List<Material>();
    }

    public class HiveTask
    {
        [Key] public long Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime DueDate { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.ToDo;
        public long GoalId { get; set; }
        public virtual Goal? Goal { get; set; }

        public long CreatorId { get; set; }
        public long? AssigneeId { get; set; }
        public string? StudentComment { get; set; }
        public string? TeacherComment { get; set; }
        [ForeignKey("AssigneeId")] public virtual User? Assignee { get; set; }

        public bool IsArtifactRequired { get; set; } = false;
        public string? ArtifactUrl { get; set; }
        public string? ResultComment { get; set; }
        public virtual ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
        public virtual ICollection<TaskCompletion> Completions { get; set; } = new List<TaskCompletion>();
    }

    public class Notification
    {
        [Key] public long Id { get; set; }
        public long UserId { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
        public string? Type { get; set; }
        public string? Data { get; set; }
        public long? TaskId { get; set; }
        public long? RoadmapStepId { get; set; }
        [ForeignKey("UserId")] public virtual User? User { get; set; }
    }

    public class TaskComment
    {
        [Key] public long Id { get; set; }
        public long TaskId { get; set; }
        [ForeignKey("TaskId")] public virtual HiveTask? Task { get; set; }
        public long UserId { get; set; }
        [ForeignKey("UserId")] public virtual User? User { get; set; }
        public string Text { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Material
    {
        [Key] public long Id { get; set; }
        public string Title { get; set; } = null!;
        public string Content { get; set; } = null!;
        public MaterialType Type { get; set; }
        public long GoalId { get; set; }
        public long? TaskId { get; set; }
        public long CreatorId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("CreatorId")] public virtual User? Creator { get; set; }
        [ForeignKey("GoalId")] public virtual Goal? Goal { get; set; }
        [ForeignKey("TaskId")] public virtual HiveTask? Task { get; set; }
    }

    public class GoalCollaboration
    {
        public long GoalId { get; set; }
        public virtual Goal? Goal { get; set; }
        public long UserId { get; set; }
        public virtual User? User { get; set; }
        public bool IsConfirmed { get; set; } = false;
        public bool IsAdmin { get; set; } = false;
    }

    public class Group
    {
        [Key] public long Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsSolo { get; set; }
        public long OwnerId { get; set; }
        [ForeignKey("OwnerId")] public virtual User? Owner { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool OwnerFinished { get; set; } = false;
        public bool PartnerFinished { get; set; } = false;

        public virtual ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
        public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
        public virtual ICollection<RoadmapStep> RoadmapSteps { get; set; } = new List<RoadmapStep>();
    }

    public class GroupMember
    {
        public long GroupId { get; set; }
        public virtual Group? Group { get; set; }
        public long UserId { get; set; }
        public virtual User? User { get; set; }
    }

    public class RoadmapStep
    {
        [Key] public long Id { get; set; }
        public long GroupId { get; set; }
        [ForeignKey("GroupId")] public virtual Group? Group { get; set; }
        public string Content { get; set; } = null!;
        public long CreatorId { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TaskStatus Status { get; set; } = TaskStatus.ToDo;
        public int MaxAttempts { get; set; } = 3;
        public int UsedAttempts { get; set; } = 0;
        public double? TestScore { get; set; }
        public string? ArtifactUrl { get; set; }
        public string? TeacherComment { get; set; }
        public string? StudentComment { get; set; }
        public string? InstructionUrl { get; set; }
        public bool IsTest { get; set; } = false;
        public string? TestData { get; set; }
        public bool IsRequired { get; set; } = true;
        public bool IsArchived { get; set; } = false;
        public virtual ICollection<RoadmapStepComment> StepComments { get; set; } = new List<RoadmapStepComment>();
    }

    public class RoadmapStepComment
    {
        [Key] public long Id { get; set; }
        public long RoadmapStepId { get; set; }
        [ForeignKey("RoadmapStepId")] public virtual RoadmapStep? RoadmapStep { get; set; }
        public long UserId { get; set; }
        [ForeignKey("UserId")] public virtual User? User { get; set; }
        [Required] public string Text { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public class ChatRequest
    {
        [Key] public long Id { get; set; }
        public long SenderId { get; set; }
        [ForeignKey("SenderId")] public virtual User? Sender { get; set; }
        public long ReceiverId { get; set; }
        [ForeignKey("ReceiverId")] public virtual User? Receiver { get; set; }
        public RequestStatus Status { get; set; } = RequestStatus.Pending;
    }

    public class Friendship
    {
        [Key] public long Id { get; set; }
        public long UserOneId { get; set; }
        public long UserTwoId { get; set; }
        public bool IsAccepted { get; set; }
        [ForeignKey("UserOneId")] public virtual User? UserOne { get; set; }
        [ForeignKey("UserTwoId")] public virtual User? UserTwo { get; set; }
    }

    public class Skill { [Key] public long Id { get; set; } public string Name { get; set; } = null!; }

    public class UserSkill
    {
        public long UserId { get; set; }
        public long SkillId { get; set; }
        public SkillType Type { get; set; }
        public virtual Skill? Skill { get; set; }
        public bool IsAiVerified { get; set; } = false;
        public DateTime? VerifiedAt { get; set; }
    }

    public class ChatMessage
    {
        [Key] public long Id { get; set; }
        public long GroupId { get; set; }
        [ForeignKey("GroupId")] public virtual Group? Group { get; set; }
        public long SenderId { get; set; }
        [ForeignKey("SenderId")] public virtual User? Sender { get; set; }
        [Required] public string Content { get; set; } = null!;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsPinned { get; set; } = false;
        public bool IsDeleted { get; set; } = false;
        public bool IsRead { get; set; } = false;
    }

    public class Review
    {
        [Key] public long Id { get; set; }
        public long ReviewerId { get; set; }
        public long ReviewedId { get; set; }
        public short Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [ForeignKey("ReviewerId")] public virtual User? Reviewer { get; set; }
        [ForeignKey("ReviewedId")] public virtual User? Reviewed { get; set; }
    }

    public class Event
    {
        [Key] public long Id { get; set; }
        [Required] public string Title { get; set; } = null!;
        public string? Description { get; set; }
        [Required] public DateTime EventDate { get; set; }
        public bool IsCompleted { get; set; }
        public string? LinkUrl { get; set; }
        public string? Location { get; set; }
        public string? ImageUrl { get; set; }
        [Required] public long CreatorId { get; set; }
        [ForeignKey("CreatorId")] public virtual User? Creator { get; set; }
        public long? GroupId { get; set; }
        [ForeignKey("GroupId")] public virtual Group? Group { get; set; }
    }

    public class TaskCompletion
    {
        [Key] public long Id { get; set; }
        public long TaskId { get; set; }
        [ForeignKey("TaskId")] public virtual HiveTask? Task { get; set; }
        public long UserId { get; set; }
        [ForeignKey("UserId")] public virtual User? User { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}