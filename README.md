# CalcpadServer with MinIO Blob Storage

SQLite Database API for Calcpad modules with self-hosted blob storage capabilities.

## Features

- Self-hosted MinIO blob storage container
- **Multi-user authentication and authorization** with JWT tokens
- **Role-based access control** (Viewer, Contributor, Admin)
- **Immutable file versioning** - files cannot be modified, only new versions created
- **Automatic version assignment** - version 1 on upload, incremental versions on updates
- **Custom metadata and tags support** for blob objects  
- **Structured metadata fields** for file lifecycle management
- Docker Compose setup for easy deployment
- RESTful endpoints for versioned file operations
- Object-level metadata management with S3-compatible API

## Quick Start

### 1. Start MinIO Container

```bash
docker-compose up -d
```

This will start MinIO on:
- API: http://localhost:9000
- Web Console: http://localhost:9001
- Default credentials: `calcpad-admin` / `calcpad-password-123`

### 2. Run the API

```bash
cd CalcpadServer.Api
dotnet run
```

The API will be available at:
- API: http://localhost:5000
- Swagger: http://localhost:5000/swagger

### 3. Default Admin User

A default admin user is created automatically:
- **Username**: `admin`
- **Password**: `admin123`
- **Role**: Admin

Use these credentials to log in and manage other users.

## User Roles & Permissions

### Role Hierarchy
- **Viewer** (Read-only): Can view and download all files and metadata
- **Contributor** (Read/Write): Can upload files, create new versions, update metadata and tags
- **Admin** (Full control): Can delete files, manage users, and update user roles

### Permission Matrix
| Action | Viewer | Contributor | Admin |
|--------|--------|-------------|-------|
| View files & metadata | ✅ | ✅ | ✅ |
| Download files | ✅ | ✅ | ✅ |
| Upload new files | ❌ | ✅ | ✅ |
| Create new versions | ❌ | ✅ | ✅ |
| Update tags/metadata | ❌ | ✅ | ✅ |
| Delete files | ❌ | ❌ | ✅ |
| Manage users | ❌ | ❌ | ✅ |
| Update user roles | ❌ | ❌ | ✅ |

## API Endpoints

### Authentication

- **POST** `/api/auth/login` - User login (returns JWT token)
- **POST** `/api/auth/register` - Register new user (Admin only)
- **GET** `/api/auth/profile` - Get current user profile

### User Management (Admin Only)

- **GET** `/api/user` - List all users
- **GET** `/api/user/{userId}` - Get specific user
- **PUT** `/api/user/{userId}/role` - Update user role
- **DELETE** `/api/user/{userId}` - Delete user

### File Operations (Immutable Versioning)

- **POST** `/api/blobstorage/upload` - Upload a file (creates version 1)
- **POST** `/api/blobstorage/upload-simple` - Upload a file (simple version)
- **POST** `/api/blobstorage/new-version/{baseFileName}` - Create new version of existing file
- **GET** `/api/blobstorage/download/{fileName}` - Download specific version
- **GET** `/api/blobstorage/download-latest/{baseFileName}` - Download latest version
- **DELETE** `/api/blobstorage/delete/{fileName}` - Delete specific version
- **DELETE** `/api/blobstorage/all-versions/{baseFileName}` - Delete all versions
- **GET** `/api/blobstorage/list` - List all files (names only)
- **GET** `/api/blobstorage/list-with-metadata` - List files with full metadata
- **GET** `/api/blobstorage/exists/{fileName}` - Check if file exists

### Version Management

- **GET** `/api/blobstorage/versions/{baseFileName}` - List all versions of a file
- **GET** `/api/blobstorage/latest/{baseFileName}` - Get latest version metadata
- **GET** `/api/blobstorage/metadata/{fileName}` - Get specific version metadata

### Tags (Mutable)

- **GET** `/api/blobstorage/tags/{fileName}` - Get file tags
- **PUT** `/api/blobstorage/tags/{fileName}` - Update file tags
- **DELETE** `/api/blobstorage/tags/{fileName}` - Remove all file tags

### Example Usage

#### 1. Login to get JWT token
```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}' \
  http://localhost:5000/api/auth/login

# Response: {"token": "eyJ...", "user": {...}, "expiresAt": "..."}
```

#### 2. Upload a new file (creates version 1 automatically)
**Note**: Include the JWT token in the Authorization header for all subsequent requests.
```bash
curl -X POST \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -F "file=@document.pdf" \
  -F "structuredMetadata[originalFileName]=document.pdf" \
  -F "structuredMetadata[dateCreated]=2024-01-15T10:00:00Z" \
  -F "structuredMetadata[createdBy]=John Doe" \
  -F "tags[category]=document" \
  -F "tags[status]=draft" \
  http://localhost:5000/api/blobstorage/upload

# Response: { "versionedFileName": "document_v1.pdf", "version": 1, "baseFileName": "document.pdf" }
```

#### 3. Create a new version of existing file
```bash
curl -X POST \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -F "file=@document_updated.pdf" \
  -F "structuredMetadata[updatedBy]=Jane Smith" \
  -F "structuredMetadata[dateUpdated]=2024-01-16T14:30:00Z" \
  -F "tags[status]=review" \
  http://localhost:5000/api/blobstorage/new-version/document.pdf

# Response: { "versionedFileName": "document_v2.pdf", "version": 2, "baseFileName": "document.pdf" }
```

#### Upload a file with form data (legacy)
```bash
curl -X POST \
  -F "file=@example.txt" \
  -F "metadata[description]=Sample text file" \
  -F "metadata[author]=John Doe" \
  -F "tags[category]=document" \
  -F "tags[status]=draft" \
  http://localhost:5000/api/blobstorage/upload
```

#### Upload a file (simple)
```bash
curl -X POST -F "file=@example.txt" http://localhost:5000/api/blobstorage/upload-simple
```

#### Get file metadata
```bash
curl http://localhost:5000/api/blobstorage/metadata/example.txt
```

#### 4. List all versions of a file
```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  http://localhost:5000/api/blobstorage/versions/document.pdf

# Response: [{ "fileName": "document_v1.pdf", "version": "1", ... }, { "fileName": "document_v2.pdf", "version": "2", ... }]
```

#### 5. Register a new user (Admin only)
```bash
curl -X POST \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"username": "contributor1", "email": "contrib@example.com", "password": "password123", "role": 2}' \
  http://localhost:5000/api/auth/register
```

#### 6. Download latest version
```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  http://localhost:5000/api/blobstorage/download-latest/document.pdf --output latest-document.pdf
```

#### Update file tags
```bash
curl -X PUT \
  -H "Content-Type: application/json" \
  -d '{"tags":{"category":"report","status":"published"}}' \
  http://localhost:5000/api/blobstorage/tags/example.txt
```

#### Download a file
```bash
curl http://localhost:5000/api/blobstorage/download/example.txt --output downloaded-file.txt
```

#### List files with metadata
```bash
curl http://localhost:5000/api/blobstorage/list-with-metadata
```

## Configuration

### Environment Variables (.env)
```
MINIO_ENDPOINT=localhost:9000
MINIO_ACCESS_KEY=calcpad-admin
MINIO_SECRET_KEY=calcpad-password-123
MINIO_BUCKET_NAME=calcpad-storage
MINIO_USE_SSL=false
```

### Application Settings (appsettings.json)
```json
{
  "MinIO": {
    "Endpoint": "localhost:9000",
    "AccessKey": "calcpad-admin",
    "SecretKey": "calcpad-password-123",
    "BucketName": "calcpad-storage",
    "UseSSL": false
  }
}
```

## MinIO Web Console

Access the MinIO web console at http://localhost:9001 with credentials:
- Username: `calcpad-admin`
- Password: `calcpad-password-123`

## Development

### Prerequisites
- .NET 8.0 SDK
- Docker & Docker Compose

### Project Structure
```
CalcpadServer/
├── docker-compose.yml          # MinIO container setup
├── .env                        # Environment variables
├── CalcpadServer.Api/          # C# Web API
│   ├── Controllers/
│   │   └── BlobStorageController.cs
│   ├── Services/
│   │   └── BlobStorageService.cs
│   ├── Models/
│   │   └── BlobMetadata.cs     # DTOs for metadata operations
│   └── Program.cs
└── README.md
```

## Metadata and Tags

### Structured Metadata
Predefined metadata fields for common file management use cases:

- **originalFileName** - Original name of the uploaded file
- **dateCreated** - When the file was initially created
- **dateUpdated** - Last modification date
- **version** - Version number or identifier
- **createdBy** - User who created the file
- **updatedBy** - User who last updated the file  
- **dateReviewed** - When the file was reviewed
- **reviewedBy** - User who reviewed the file
- **testedBy** - User who tested the file
- **dateTested** - When the file was tested

### Custom Metadata
- Store key-value pairs with objects using `x-amz-meta-` prefix
- Metadata is stored separately from object data for better performance
- Useful for application-specific attributes like descriptions, categories, processing status

### Object Tags
- Up to 10 custom tags per object
- Perfect for categorization, search, and access control
- Tag keys: 1-128 UTF-8 characters, values: 0-256 UTF-8 characters
- Examples: `category=document`, `status=published`, `department=finance`

### Immutable Versioning Workflow

1. **Upload**: Files are automatically assigned version 1
2. **Update**: Create new versions instead of modifying existing files
3. **Track**: Each version maintains its own metadata and history
4. **Manage**: List, download, and delete versions independently

### Use Cases
- **Document Lifecycle Management**: Track versions, authors, reviewers with immutable history
- **Quality Assurance**: Record testing and review dates/users for each version
- **Audit Compliance**: Maintain complete, unalterable file history
- **Version Control**: Access any previous version of a file
- **Collaboration**: Multiple users can work on versions without conflicts

### Dependencies
- `Minio` - MinIO .NET client library
- `Microsoft.AspNetCore.App` - ASP.NET Core framework

## Production Considerations

1. **Security**: Change default credentials in production
2. **SSL**: Enable SSL/TLS for production deployments
3. **Backup**: Configure MinIO data persistence and backup strategies
4. **Monitoring**: Add health checks and monitoring
5. **Scaling**: Consider MinIO distributed mode for high availability