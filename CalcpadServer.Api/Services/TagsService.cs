using CalcpadServer.Api.Data;
using CalcpadServer.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CalcpadServer.Api.Services;

public interface ITagsService
{
    Task<IEnumerable<PreDefinedTag>> GetAllTagsAsync();
    Task<PreDefinedTag> CreateTagAsync(string name, UserContext userContext);
    Task<bool> DeleteTagAsync(int id, UserContext userContext);
}

public class TagsService : ITagsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TagsService> _logger;

    public TagsService(ApplicationDbContext context, ILogger<TagsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<PreDefinedTag>> GetAllTagsAsync()
    {
        try
        {
            var tags = await _context.PreDefinedTags
                .OrderBy(t => t.Name)
                .ToListAsync();
            
            _logger.LogInformation("Retrieved {Count} tags", tags.Count);
            return tags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tags");
            throw;
        }
    }

    public async Task<PreDefinedTag> CreateTagAsync(string name, UserContext userContext)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tag name cannot be empty", nameof(name));
            }

            if (name.Length > 100)
            {
                throw new ArgumentException("Tag name cannot exceed 100 characters", nameof(name));
            }

            // Check if tag already exists
            var existingTag = await _context.PreDefinedTags
                .FirstOrDefaultAsync(t => t.Name.ToLower() == name.ToLower());
            
            if (existingTag != null)
            {
                throw new ArgumentException($"Tag '{name}' already exists");
            }

            var tag = new PreDefinedTag
            {
                Name = name.Trim()
            };

            _context.PreDefinedTags.Add(tag);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created tag '{TagName}' with ID {TagId} by user {UserId}", 
                tag.Name, tag.Id, userContext.UserId);
            
            return tag;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tag '{TagName}'", name);
            throw;
        }
    }

    public async Task<bool> DeleteTagAsync(int id, UserContext userContext)
    {
        try
        {
            var tag = await _context.PreDefinedTags.FindAsync(id);
            
            if (tag == null)
            {
                _logger.LogWarning("Attempted to delete non-existent tag with ID {TagId}", id);
                return false;
            }

            _context.PreDefinedTags.Remove(tag);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted tag '{TagName}' (ID: {TagId}) by user {UserId}", 
                tag.Name, tag.Id, userContext.UserId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tag with ID {TagId}", id);
            throw;
        }
    }
}