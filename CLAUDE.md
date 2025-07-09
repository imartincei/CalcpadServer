# CalcpadServer Codebase Documentation

## Project Overview
CalcpadServer is a **self-hosted file management system** with MinIO blob storage and multi-user authentication. It provides a complete document lifecycle management solution for Calcpad modules with immutable versioning, role-based access control, and comprehensive metadata management.

### Architecture
- **Backend**: ASP.NET Core 8.0 Web API with Entity Framework Core + SQLite
- **Storage**: MinIO object storage with dual-bucket architecture (working/stable)
- **Authentication**: JWT-based with support for Local/OIDC/SAML providers
- **Desktop Client**: WPF application (CalcpadViewer) for Windows-based file management
- **Deployment**: Docker Compose with automatic initialization

---

## Recent Work
- ✅ COMPLETED: Replaced multi-select dropdown with filter button and modal for tag selection
- ✅ COMPLETED: Fixed tag filtering functionality - now works correctly with category filtering
- ✅ COMPLETED: Added API endpoint `/api/BlobStorage/base64/{fileName}` to get base64 representation of photos/files
- ✅ COMPLETED: Fixed StackOverflowException in filtering logic by eliminating circular dependencies
- ✅ COMPLETED: Tags now load immediately upon connection instead of requiring navigation to tags tab
- Don't try to build CalcpadViewer, I will build it to test
- CalcpadViewer cannot run on Linux. Do not try to compile it
- Code Analysis: Prefer comparing 'Count' to 0 rather than using 'Any()' for clarity and performance

## UI/UX Improvements
- Tag filtering now uses a modal dialog similar to file tagging interface
- Filter button shows "Filter Tags" and displays count of selected tags
- Tags are loaded automatically when admin user connects
- Both tag and category filters work independently and together

---

## System Architecture

### Core Components

#### 1. **CalcpadServer.Api** (Backend Web API)
```
CalcpadServer.Api/
├── Controllers/           # API endpoint controllers
│   ├── AuthController.cs         # Authentication (login/register)
│   ├── AuthInfoController.cs     # Authentication info
│   ├── BlobStorageController.cs  # File operations
│   ├── TagsController.cs         # Tag management
│   └── UserController.cs         # User management
├── Services/              # Business logic layer
│   ├── BlobStorageService.cs     # MinIO operations
│   ├── TagsService.cs           # Tag CRUD operations
│   └── UserService.cs           # User management & JWT auth
├── Models/               # Data models & DTOs
│   ├── User.cs                  # User entities & auth models
│   ├── BlobMetadata.cs          # File metadata models
│   └── AuthenticationConfig.cs  # Auth configuration
├── Data/                 # Database context
│   └── ApplicationDbContext.cs  # EF Core context
├── Middleware/           # Custom middleware
│   └── AuthMiddleware.cs        # JWT token extraction
├── Attributes/           # Custom attributes
│   └── AuthorizeRoleAttribute.cs # Role-based authorization
└── Extensions/           # Extension methods
    └── AuthenticationExtensions.cs # Auth provider setup
```

#### 2. **CalcpadViewer** (Desktop WPF Client)
```
CalcpadViewer/
├── MainWindow.xaml/cs    # Main application window
├── Models.cs             # Shared data models
├── ViewModels/           # MVVM view models
│   └── MainViewModel.cs  # Main window view model
├── UserService.cs        # HTTP client for API calls
├── Dialogs/              # Modal dialogs
│   ├── UploadDialog.xaml/cs      # File upload dialog
│   ├── AddUserDialog.xaml/cs     # User creation dialog
│   ├── TagFilterDialog.xaml/cs   # Tag filtering modal
│   └── FileVersionsWindow.xaml/cs # File versions viewer
└── ViewerSettings.json   # Application settings
```

#### 3. **Infrastructure**
- **Docker Compose**: Orchestrates MinIO, SQLite initialization, and API
- **MinIO**: Dual-bucket object storage (working/stable) with versioning
- **SQLite**: User management and predefined tags database

---

## Data Architecture

### Database Schema (SQLite)

#### Users Table
```sql
CREATE TABLE Users (
    Id TEXT PRIMARY KEY,
    Username TEXT NOT NULL UNIQUE,
    Email TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    Role INTEGER NOT NULL DEFAULT 2,  -- 1=Viewer, 2=Contributor, 3=Admin
    CreatedAt TEXT NOT NULL,
    LastLoginAt TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1
);
```

#### PreDefinedTags Table
```sql
CREATE TABLE PreDefinedTags (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE
);
```

### MinIO Object Storage

#### Bucket Structure
- **calcpad-storage-working**: Active development files
- **calcpad-storage-stable**: Production/finalized files
- **Versioning**: Enabled on both buckets for immutable history

#### File Metadata Storage
- **MinIO Headers**: Custom metadata stored as `x-amz-meta-*` headers
- **Object Tags**: Up to 10 key-value tags per file for categorization
- **Structured Metadata**: Predefined lifecycle fields (created, updated, reviewed, tested)

---

## Authentication & Authorization

### Multi-Provider Authentication System

#### Supported Providers
1. **Local Authentication** (Default)
   - JWT tokens with configurable expiry
   - BCrypt password hashing
   - User registration/management via API

2. **OIDC (OpenID Connect)**
   - Enterprise SSO integration
   - Configurable authority, client ID/secret
   - Token-based authentication

3. **SAML** (Future implementation)
   - SAML 2.0 support planned
   - Enterprise identity provider integration

#### Role-Based Access Control
- **Viewer (1)**: Read-only access to all files and metadata
- **Contributor (2)**: Upload files, create versions, update metadata/tags
- **Admin (3)**: Full control + user management + file deletion

#### Security Implementation
- **AuthMiddleware**: JWT token extraction and validation
- **AuthorizeRoleAttribute**: Controller-level role enforcement
- **UserContext**: Request-scoped user information

---

## API Endpoints

### Authentication Endpoints
| Method | Endpoint | Description | Role Required |
|--------|----------|-------------|---------------|
| POST | `/api/auth/login` | User login | None |
| POST | `/api/auth/register` | User registration | Admin |
| GET | `/api/auth/profile` | Current user profile | Any |

### File Management Endpoints
| Method | Endpoint | Description | Role Required |
|--------|----------|-------------|---------------|
| POST | `/api/blobstorage/upload` | Upload file | Contributor |
| GET | `/api/blobstorage/download/{fileName}` | Download file | Viewer |
| GET | `/api/blobstorage/base64/{fileName}` | Get file as base64 | Viewer |
| DELETE | `/api/blobstorage/delete/{fileName}` | Delete file | Admin |
| GET | `/api/blobstorage/list` | List files | Viewer |
| GET | `/api/blobstorage/list-with-metadata` | List files with metadata | Viewer |
| GET | `/api/blobstorage/metadata/{fileName}` | Get file metadata | Viewer |
| GET | `/api/blobstorage/versions/{fileName}` | Get file versions | Viewer |

### Tag Management Endpoints
| Method | Endpoint | Description | Role Required |
|--------|----------|-------------|---------------|
| GET | `/api/tags` | List all predefined tags | Viewer |
| POST | `/api/tags` | Create new tag | Admin |
| DELETE | `/api/tags/{id}` | Delete tag | Admin |
| GET | `/api/blobstorage/tags/{fileName}` | Get file tags | Viewer |
| PUT | `/api/blobstorage/tags/{fileName}` | Update file tags | Contributor |

### User Management Endpoints
| Method | Endpoint | Description | Role Required |
|--------|----------|-------------|---------------|
| GET | `/api/user` | List all users | Admin |
| GET | `/api/user/{userId}` | Get specific user | Admin |
| PUT | `/api/user/{userId}` | Update user | Admin |
| DELETE | `/api/user/{userId}` | Delete user | Admin |

---

## Technical Implementation Details

### Desktop Client Architecture (CalcpadViewer)

#### MVVM Pattern Implementation
- **MainViewModel**: Centralized state management with ObservableCollections
- **Data Binding**: Two-way binding between UI and ViewModel
- **Command Pattern**: Button clicks handled through event handlers
- **Service Layer**: HTTP client abstraction for API communication

#### Tag Filtering System
- **Modal Dialog**: `TagFilterDialog.xaml` provides checkbox-based selection
- **Search Functionality**: Real-time tag filtering with text input
- **Unified Filtering**: `ApplyAllFilters()` combines tag and category filters
- **State Management**: Selected tags stored in ObservableCollection

#### File Management Features
- **Dual-Bucket Display**: Files from both working and stable buckets
- **Category Filtering**: Files filtered by metadata category
- **Version Management**: View and download specific file versions
- **Metadata Display**: Comprehensive file information panel

### Backend Service Architecture

#### BlobStorageService
- **Bucket Resolution**: Automatic determination of working vs stable bucket
- **Metadata Handling**: Structured metadata with lifecycle fields
- **Version Management**: Immutable file versioning with MinIO
- **Tag Operations**: CRUD operations for object tags

#### UserService
- **JWT Token Management**: Token generation, validation, and refresh
- **Password Security**: BCrypt hashing with salt rounds
- **Role Management**: Hierarchical role system with inheritance
- **User Lifecycle**: Registration, authentication, and profile management

#### TagsService
- **Predefined Tags**: Centralized tag management via database
- **Tag Validation**: Uniqueness constraints and naming rules
- **Tag Assignment**: Association with file objects via MinIO tags

---

## Configuration & Deployment

### Environment Configuration

#### Docker Compose Services
- **minio**: MinIO object storage server
- **minio-init**: Bucket creation and versioning setup
- **sqlite-init**: Database schema initialization
- **calcpad-api**: .NET API container

#### Configuration Files
- **appsettings.json**: Application configuration
- **docker-compose.yml**: Container orchestration
- **ViewerSettings.json**: Desktop client settings

### Security Configuration
- **JWT Secret**: Minimum 32-character secret for token signing
- **MinIO Credentials**: Configurable access keys
- **Database**: SQLite with connection string configuration
- **HTTPS**: Optional SSL/TLS configuration

---

## Development Guidelines

### Code Quality Standards
- **Error Handling**: Comprehensive try-catch blocks with logging
- **Async/Await**: Proper asynchronous programming patterns
- **Dependency Injection**: Service registration and scoped lifetime
- **Logging**: Structured logging with different levels
- **Validation**: Input validation and sanitization

### Testing Approach
- **Unit Tests**: Service layer testing (planned)
- **Integration Tests**: API endpoint testing (planned)
- **Manual Testing**: Desktop client validation

### Performance Considerations
- **Streaming**: Large file handling with stream processing
- **Caching**: In-memory caching for frequently accessed data
- **Database Optimization**: Indexed queries and connection pooling
- **Pagination**: Large dataset handling (planned)

---

## MinIO Metadata Structure
- Default metadata in MinIO includes properties like:
  - ContentType (null)
  - ETag: "b01002cd9dc31871140a68a321f804c8"
  - Key: "ExcelTabletoCalcPad.txt"
  - LastModified: "2025-07-01T19:07:12.573Z"
  - LastModifiedDateTime: {7/1/2025 15:07:12}
  - Size: 3276 bytes
  - IsDir: false
  - IsLatest: false

---

## Future Enhancements
- **SAML Authentication**: Complete SAML 2.0 implementation
- **Advanced Search**: Full-text search across file content and metadata
- **Audit Logging**: Comprehensive activity tracking
- **Backup/Recovery**: Automated backup strategies
- **Performance Monitoring**: Application metrics and health checks
- **Mobile Client**: Cross-platform mobile application