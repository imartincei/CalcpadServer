using CalcpadServer.Api.Attributes;
using CalcpadServer.Api.Models;
using CalcpadServer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CalcpadServer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AuthorizeRole(UserRole.Viewer)]
public class BlobStorageController : ControllerBase
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<BlobStorageController> _logger;

    public BlobStorageController(IBlobStorageService blobStorageService, ILogger<BlobStorageController> logger)
    {
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [AuthorizeRole(UserRole.Contributor)]
    public async Task<IActionResult> UploadFile([FromForm] UploadRequest request)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            using var stream = request.File.OpenReadStream();
            var fileName = await _blobStorageService.UploadFileAsync(
                request.File.FileName, 
                stream, 
                userContext,
                request.File.ContentType,
                request.Tags,
                request.Metadata);
            
            return Ok(new { FileName = fileName, Message = "File uploaded successfully"});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", request.File.FileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> DownloadFile(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var fileStream = await _blobStorageService.DownloadFileAsync(fileName, userContext);
            return File(fileStream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("delete/{fileName}")]
    [AuthorizeRole(UserRole.Admin)]
    public async Task<IActionResult> DeleteFile(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var deleted = await _blobStorageService.DeleteFileAsync(fileName, userContext);
            
            if (deleted)
            {
                return Ok(new { Message = $"File '{fileName}' deleted successfully" });
            }
            
            return StatusCode(500, "Failed to delete file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListFiles()
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            var files = await _blobStorageService.ListFilesAsync(userContext);
            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("list-with-metadata")]
    public async Task<IActionResult> ListFilesWithMetadata()
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            var files = await _blobStorageService.ListFilesWithMetadataAsync(userContext);
            return Ok(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files with metadata");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("exists/{fileName}")]
    public async Task<IActionResult> FileExists(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            var exists = await _blobStorageService.FileExistsAsync(fileName, userContext);
            return Ok(new { FileName = fileName, Exists = exists });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking file existence {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("metadata/{fileName}")]
    public async Task<IActionResult> GetFileMetadata(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var metadata = await _blobStorageService.GetFileMetadataAsync(fileName, userContext);
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metadata for file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }


    [HttpGet("tags/{fileName}")]
    public async Task<IActionResult> GetFileTags(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var tags = await _blobStorageService.GetFileTagsAsync(fileName, userContext);
            return Ok(new { FileName = fileName, Tags = tags });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tags for file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("tags/{fileName}")]
    public async Task<IActionResult> SetFileTags(string fileName, [FromBody] TagsUpdateRequest request)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var success = await _blobStorageService.SetFileTagsAsync(fileName, request.Tags, userContext);
            
            if (success)
            {
                return Ok(new { Message = $"Tags updated for file '{fileName}'" });
            }
            
            return StatusCode(500, "Failed to update tags");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting tags for file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("tags/{fileName}")]
    public async Task<IActionResult> DeleteFileTags(string fileName)
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            
            if (!await _blobStorageService.FileExistsAsync(fileName, userContext))
            {
                return NotFound($"File '{fileName}' not found");
            }

            var success = await _blobStorageService.DeleteFileTagsAsync(fileName, userContext);
            
            if (success)
            {
                return Ok(new { Message = $"Tags removed for file '{fileName}'" });
            }
            
            return StatusCode(500, "Failed to remove tags");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tags for file {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }





}