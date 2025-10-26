# S3 Mount Manager

## Overview
S3 Mount Manager allows you to mount your S3-compatible buckets as native Windows drives. The application supports multiple S3 providers including AWS S3, Backblaze B2, Google Cloud Storage, Wasabi, DigitalOcean Spaces, MinIO, and custom S3-compatible providers.

## Features
- **Multiple S3 Providers**: Pre-configured templates for popular S3 providers
- **Secure Credential Storage**: Credentials encrypted using Windows DPAPI and stored in Credential Manager
- **Virtual Drive Mounting**: Mount S3 buckets as Windows drives with custom drive letters
- **Custom Icons**: Set custom PNG/ICO icons for your mounted drives
- **System Tray Integration**: Runs in background with system tray support
- **Auto-Mount**: Configure buckets to automatically mount on application startup
- **Stream-Only Access**: Direct streaming from S3 without local caching
- **Modern Dark UI**: Sleek, Cloudflare-inspired dark theme interface

## Quick Start

### 1. Running the Application
Simply run the application - **no icon file is required**! The app will:
- ? Start successfully with or without an icon
- ? Use Windows default system tray icon if no custom icon found
- ? Work exactly the same regardless of icon presence

### 2. Adding a Custom Icon (Optional)
If you want a custom system tray icon:
1. Create or download a 32x32 `.ico` file
2. Place it at `S3Mount\Resources\icon.ico`
3. Rebuild the project

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for detailed icon setup instructions.

## Setup Instructions

### Configuring Your First Mount
1. Launch the application
2. Click "Add Mount" or "Configure Your First Mount"
3. Select your S3 provider from the dropdown
4. Fill in the required fields:
   - Mount Name: Friendly name for the drive
   - Bucket Name: Your S3 bucket name
   - Service URL: Automatically filled based on provider
   - Region: Your bucket's region
   - Access Key: Your S3 access key
   - Secret Key: Your S3 secret key
5. (Optional) Set a custom drive letter and icon
6. Click "Test Connection" to verify settings
7. Click "Save" to save the configuration

### Mounting a Bucket
1. Click the "Mount" button next to the configured mount
2. The bucket will be mounted as a Windows drive
3. Access your files through Windows Explorer

## Important Notes

### Virtual File System Implementation
The current implementation uses Windows `subst` command as a placeholder. For production use, you should implement a proper virtual file system using one of these technologies:

1. **Windows Projected File System (ProjFS)** - Recommended for Windows 10+
   - NuGet: `Microsoft.Windows.ProjFS`
   - Provides on-demand file hydration
   - Native Windows support

2. **Dokan** - Open-source user-mode file system
   - NuGet: `DokanNet`
   - Cross-platform support
   - More complex implementation

3. **CBFS (Callback File System)** - Commercial solution
   - Feature-rich
   - Professional support

### S3 Immutability Handling
Since S3 is immutable storage, the application handles file updates by:
1. Deleting the old object
2. Creating a new object with updated content
3. Maintaining the same key/path

### Performance Considerations
- Files are streamed directly from S3 (no local caching)
- Large files may take time to open
- Network latency affects file operations
- Consider implementing read-ahead buffering for better performance

## Architecture

### Services
- **CredentialService**: Manages secure storage of S3 credentials using Windows DPAPI
- **S3Service**: Handles all S3 operations (list, get, put, delete)
- **VirtualDriveService**: Manages drive mounting and unmounting

### Models
- **S3MountConfiguration**: Stores mount configuration
- **S3ProviderTemplate**: Pre-configured provider templates

### ViewModels
- **MainViewModel**: Manages the main window and mount list
- **MountConfigurationViewModel**: Handles mount configuration dialog

## Security
- All credentials are encrypted using Windows DPAPI
- Encrypted data stored in Windows Credential Manager
- Credentials never written to disk in plain text
- User-specific encryption (other users cannot access)

## Troubleshooting

### Mount Failed
- Verify your credentials are correct
- Check network connectivity
- Ensure bucket exists and is accessible
- Verify region is correct
- For non-AWS providers, ensure "Force Path Style" is enabled if required

### Drive Not Appearing
- Check if drive letter is already in use
- Try a different drive letter
- Run as Administrator if needed

### Connection Test Fails
- Verify service URL is correct
- Check firewall settings
- Ensure bucket name is correct
- Verify access key has proper permissions

### Icon Not Showing
- Icon file missing: App will use default Windows icon
- Icon file corrupted: Check file format (must be .ico)
- Icon not copied: Clean and rebuild the solution
- See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for icon setup help

## Future Enhancements
- [ ] Implement ProjFS for true virtual file system
- [ ] Add file caching options
- [ ] Support for multi-part uploads
- [ ] Bandwidth throttling
- [ ] File versioning support
- [ ] Sync status indicators
- [ ] Upload/download progress tracking
- [ ] Search functionality
- [ ] Multi-bucket browsing

## License
This project is provided as-is for educational and development purposes.
