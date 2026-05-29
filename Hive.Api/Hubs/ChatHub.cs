using Microsoft.AspNetCore.SignalR;

namespace Hive.Api.Hubs
{
    public class ChatHub : Hub
    {
        // Пользователь заходит в комнату группы
        public async Task JoinGroup(string groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
        }

        // Пользователь выходит из комнаты
        public async Task LeaveGroup(string groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }
    }
}