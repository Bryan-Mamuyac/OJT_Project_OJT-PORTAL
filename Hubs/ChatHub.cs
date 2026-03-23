using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace ITPMS_OJT.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        // Called by client when they open a conversation
        public async Task JoinConversation(string conversationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "conv_" + conversationId);
        }

        // Called by client when they close/leave a conversation
        public async Task LeaveConversation(string conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "conv_" + conversationId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // SignalR automatically removes from all groups on disconnect
            await base.OnDisconnectedAsync(exception);
        }
    }
}