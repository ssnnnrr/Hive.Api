using Hive.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hive.Api.Data
{
    public class HiveDbContext : DbContext
    {
        public HiveDbContext(DbContextOptions<HiveDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Goal> Goals { get; set; }
        public DbSet<HiveTask> Tasks { get; set; }
        public DbSet<Material> Materials { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<TaskCompletion> TaskCompletions { get; set; }
        public DbSet<GroupMember> GroupMembers { get; set; }
        public DbSet<Skill> Skills { get; set; }
        public DbSet<UserSkill> UserSkills { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<ChatRequest> ChatRequests { get; set; }
        public DbSet<Friendship> Friendships { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<RoadmapStep> RoadmapSteps { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<TaskComment> TaskComments { get; set; }
        public DbSet<RoadmapStepComment> StepComments { get; set; }
        public DbSet<GoalCollaboration> GoalCollaborations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Ключи для составных таблиц
            modelBuilder.Entity<UserSkill>().HasKey(us => new { us.UserId, us.SkillId, us.Type });
            modelBuilder.Entity<GroupMember>().HasKey(gm => new { gm.UserId, gm.GroupId });
            modelBuilder.Entity<GoalCollaboration>().HasKey(gc => new { gc.GoalId, gc.UserId });

            // Уникальный индекс для завершения задач
            modelBuilder.Entity<TaskCompletion>().HasIndex(tc => new { tc.TaskId, tc.UserId }).IsUnique();

            // Настройка отзывов
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Reviewer)
                .WithMany()
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.Reviewed)
                .WithMany(u => u.ReviewsReceived)
                .HasForeignKey(r => r.ReviewedId)
                .OnDelete(DeleteBehavior.Restrict);

            // Настройка материалов (исправлено)
            modelBuilder.Entity<Material>(entity =>
            {
                entity.HasOne(m => m.Task)
                      .WithMany()
                      .HasForeignKey(m => m.TaskId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(m => m.Creator)
                      .WithMany()
                      .HasForeignKey(m => m.CreatorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Настройка дружбы
            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.UserOne)
                .WithMany()
                .HasForeignKey(f => f.UserOneId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.UserTwo)
                .WithMany()
                .HasForeignKey(f => f.UserTwoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Комментарии к шагам дорожной карты
            modelBuilder.Entity<RoadmapStepComment>()
                .HasOne(c => c.RoadmapStep)
                .WithMany(s => s.StepComments)
                .HasForeignKey(c => c.RoadmapStepId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}