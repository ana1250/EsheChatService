using EsheChatService.Models;

namespace EsheChatService.Services.Folders
{
    public interface IFolderService
    {
        Task<ChatFolder> CreateAsync(string name, Guid userId);
        Task RenameAsync(ChatFolder folder, string newName);
        Task DeleteAsync(ChatFolder folder);
        Task DeleteWithSessionsAsync(ChatFolder folder);
        Task MoveSessionToFolderAsync(ChatSession session, Guid? folderId);
    }
}
