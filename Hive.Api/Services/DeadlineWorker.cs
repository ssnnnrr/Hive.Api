using Hive.Api.Data;
using Hive.Api.Entities;
using Hive.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hive.Api.Services
{
    public class DeadlineWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<ChatHub> _hubContext;

        public DeadlineWorker(IServiceProvider serviceProvider, IHubContext<ChatHub> hubContext)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<HiveDbContext>();

                    var nowUtc = DateTime.UtcNow;
                    var moscowNow = nowUtc.AddHours(3);
                    var todayMoscow = moscowNow.Date;

                    // 1. Проверка Событий (минута в минуту)
                    var overdueEvents = await context.Events
                        .Where(e => !e.IsCompleted && e.EventDate < nowUtc && !context.Notifications.Any(n => n.Type == "EventOverdue" && n.Data == e.Id.ToString()))
                        .ToListAsync();

                    foreach (var ev in overdueEvents)
                    {
                        context.Notifications.Add(new Notification
                        {
                            UserId = ev.CreatorId,
                            Title = "Событие пропущено! ⚠️",
                            Message = $"Вы пропустили время: {ev.Title}",
                            Type = "EventOverdue",
                            Data = ev.Id.ToString(),
                            CreatedAt = nowUtc
                        });
                    }

                    // 2. Проверка Шагов Roadmap (после окончания дня по МСК)
                    var overdueSteps = await context.RoadmapSteps
                        .Where(s => s.Status != Entities.TaskStatus.Done && s.DueDate.AddHours(3).Date < todayMoscow && !context.Notifications.Any(n => n.Type == "RoadmapOverdue" && n.Data == s.Id.ToString()))
                        .ToListAsync();

                    foreach (var step in overdueSteps)
                    {
                        var student = await context.GroupMembers.FirstOrDefaultAsync(gm => gm.GroupId == step.GroupId && gm.UserId != step.CreatorId);
                        if (student != null)
                        {
                            context.Notifications.Add(new Notification
                            {
                                UserId = student.UserId,
                                Title = "Задание просрочено! 🔔",
                                Message = $"Срок сдачи вышел: {step.Content}",
                                Type = "RoadmapOverdue",
                                Data = step.Id.ToString(),
                                CreatedAt = nowUtc
                            });
                        }
                    }

                    // 3. Проверка личных задач (Шаги к целям)
                    var overdueTasks = await context.Tasks
                        .Where(t => t.Status != Entities.TaskStatus.Done && t.DueDate.AddHours(3).Date < todayMoscow && !context.Notifications.Any(n => n.Type == "TaskOverdue" && n.Data == t.Id.ToString()))
                        .ToListAsync();

                    foreach (var task in overdueTasks)
                    {
                        context.Notifications.Add(new Notification
                        {
                            UserId = task.CreatorId,
                            Title = "Шаг к цели пропущен! ✍️",
                            Message = $"Дедлайн истек: {task.Title}",
                            Type = "TaskOverdue",
                            Data = task.Id.ToString(),
                            CreatedAt = nowUtc
                        });
                    }

                    if (context.ChangeTracker.HasChanges())
                    {
                        await context.SaveChangesAsync();
                        await _hubContext.Clients.All.SendAsync("NotificationReceived");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}