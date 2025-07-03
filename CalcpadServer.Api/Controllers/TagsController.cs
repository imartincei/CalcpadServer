using CalcpadServer.Api.Attributes;
using CalcpadServer.Api.Models;
using CalcpadServer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CalcpadServer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TagsController : ControllerBase
{
    private readonly ITagsService _tagsService;
    private readonly ILogger<TagsController> _logger;

    public TagsController(ITagsService tagsService, ILogger<TagsController> logger)
    {
        _tagsService = tagsService;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeRole(UserRole.Viewer)] // Any role can view tags
    public async Task<IActionResult> GetAllTags()
    {
        try
        {
            var tags = await _tagsService.GetAllTagsAsync();
            return Ok(tags);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tags");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    [AuthorizeRole(UserRole.Admin)] // Only admin can create tags
    public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Tag name is required");
            }

            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            var tag = await _tagsService.CreateTagAsync(request.Name, userContext);
            
            return CreatedAtAction(nameof(GetAllTags), new { id = tag.Id }, tag);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tag");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{id}")]
    [AuthorizeRole(UserRole.Admin)] // Only admin can delete tags
    public async Task<IActionResult> DeleteTag(int id)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            var success = await _tagsService.DeleteTagAsync(id, userContext);
            
            if (!success)
            {
                return NotFound($"Tag with ID {id} not found");
            }

            return Ok(new { Message = $"Tag with ID {id} deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tag with ID {TagId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}

public class CreateTagRequest
{
    public string Name { get; set; } = string.Empty;
}