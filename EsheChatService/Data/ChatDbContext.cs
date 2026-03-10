using EsheChatService.Models;
using Microsoft.EntityFrameworkCore;

namespace EsheChatService.Data
{
    public class ChatDbContext : DbContext
    {
        public ChatDbContext(DbContextOptions<ChatDbContext> options)
            : base(options) { }

        public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<ChatFolder> ChatFolders => Set<ChatFolder>();
        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<SharedSession> SharedSessions => Set<SharedSession>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            /* ---------------- ChatFolder ---------------- */

            modelBuilder.Entity<ChatFolder>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Order)
                      .IsRequired();

                entity.Property(e => e.IsExpanded)
                      .IsRequired();

                entity.HasMany(e => e.Sessions)
                      .WithOne(s => s.Folder)
                      .HasForeignKey(s => s.FolderId)
                      .OnDelete(DeleteBehavior.SetNull); // IMPORTANT
            });

            /* ---------------- ChatSession ---------------- */

            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Title)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(e => e.CreatedAt)
                      .IsRequired();

                entity.Property(e => e.UpdatedAt)
                      .IsRequired();

                entity.HasMany(e => e.Messages)
                      .WithOne()
                      .HasForeignKey(m => m.ChatSessionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            /* ---------------- ChatMessage ---------------- */

            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Role)
                      .IsRequired();

                entity.Property(e => e.Content)
                      .IsRequired();

                entity.Property(e => e.CreatedAt)
                      .IsRequired();
            });

            modelBuilder.Entity<SharedSession>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.ChatSession)
                      .WithMany(s => s.SharedWith)
                      .HasForeignKey(e => e.ChatSessionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

        }
    }
}
