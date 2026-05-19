using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.Folders
{
    public class FolderService : IFolderService
    {
        private readonly IChatRepository _repository;
        private readonly ILogger<FolderService> _logger;

        public FolderService(IChatRepository repository, ILogger<FolderService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ChatFolder> CreateAsync(string name, Guid userId)
        {
            var folder = new ChatFolder { Name = name, UserOwnerId = userId };

            await _repository.CreateFolderAsync(folder);

            _logger.LogInformation("Folder created: {FolderId} Name={FolderName} Owner={OwnerId}",
                folder.Id, folder.Name, userId);

            return folder;
        }

        public async Task RenameAsync(ChatFolder folder, string newName)
        {
            folder.Name = newName.Trim();

            await _repository.UpdateFolderAsync(folder);

            _logger.LogInformation("Folder renamed: {FolderId} NewName={FolderName}",
                folder.Id, folder.Name);
        }

        public async Task DeleteAsync(ChatFolder folder)
        {
            await _repository.DeleteFolderAsync(folder);

            _logger.LogInformation("Folder deleted: {FolderId}", folder.Id);
        }

        public async Task DeleteWithSessionsAsync(ChatFolder folder)
        {
            await _repository.DeleteFolderAndSessionsAsync(folder);

            _logger.LogInformation("Folder deleted with sessions: {FolderId}", folder.Id);
        }

        public async Task MoveSessionToFolderAsync(ChatSession session, Guid? folderId)
        {
            session.FolderId = folderId;

            await _repository.UpdateSessionAsync(session);

            _logger.LogInformation("Session {SessionId} moved to folder {FolderId}",
                session.Id, folderId);
        }
    }
}
