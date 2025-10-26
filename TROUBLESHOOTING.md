# Troubleshooting: Tray Icon Issues

## Problem: Application crashes on startup

### Solution Applied
The application has been updated to handle missing or invalid icon files gracefully. It will now:

1. **Try to load the icon from multiple locations:**
   - `bin/Debug/net9.0-windows/Resources/icon.ico`
   - `bin/Debug/net9.0-windows/icon.ico`
   - Current directory + `Resources/icon.ico`
   - Current directory + `icon.ico`

2. **Fallback behavior:**
   - If no icon is found, the application uses Windows default system tray icon
   - The application continues to run normally without crashing

3. **Error logging:**
   - Check Debug output window in Visual Studio to see which path was tried
   - Messages will indicate if icon was loaded or not found

## How to Add Your Icon

### Option 1: Use the Resources folder (Recommended)
1. Place your `icon.ico` file in: `S3Mount\Resources\icon.ico`
2. Rebuild the project
3. The icon will be copied to the output directory automatically

### Option 2: Place in project root
1. Place your `icon.ico` file in the same folder as `S3Mount.csproj`
2. Add this to `S3Mount.csproj`:
   ```xml
   <ItemGroup>
     <None Include="icon.ico">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
     </None>
   </ItemGroup>
   ```

### Option 3: Use embedded resource
1. Place icon in Resources folder
2. Update `S3Mount.csproj`:
   ```xml
   <ItemGroup>
     <Resource Include="Resources\icon.ico" />
   </ItemGroup>
   ```
3. Update App.xaml:
   ```xml
   IconSource="pack://application:,,,/Resources/icon.ico"
   ```

## Verifying Icon Location

After building, check if the icon exists in:
```
S3Mount\bin\Debug\net9.0-windows\Resources\icon.ico
```

or

```
S3Mount\bin\Release\net9.0-windows\Resources\icon.ico
```

## Icon Requirements

- **Format**: `.ico` file
- **Size**: 16x16 or 32x32 pixels (ICO files can contain multiple sizes)
- **Color depth**: 32-bit with transparency recommended
- **File size**: Keep under 50KB for best performance

## Testing

To test if your icon loads:
1. Build the application
2. Check the Debug output window in Visual Studio
3. Look for messages like:
   - `"Loaded tray icon from: [path]"` ? Success
   - `"No icon file found. Application will run with default system icon."` ?? Missing icon
   - `"Failed to load tray icon: [error]"` ? Invalid icon file

## Common Issues

### Issue: Icon shows as blank
**Cause**: ICO file is corrupted or wrong format
**Solution**: Re-export the icon using a proper ICO converter

### Issue: Icon doesn't update after changing file
**Cause**: Old icon cached in bin folder
**Solution**: Clean and rebuild the solution

### Issue: Icon works in Debug but not Release
**Cause**: Icon not copied to Release folder
**Solution**: Ensure the icon copy rule in `.csproj` is working

## Quick Test Without Icon

The application will work perfectly fine without an icon. It will just use the Windows default system tray icon. This is not a critical error.

To run without worrying about the icon:
1. Simply start the application
2. It will use the default Windows icon in the system tray
3. All functionality works normally

## Creating a Simple Icon

If you want a quick icon to test with:

### Using Paint (Windows)
1. Create a 32x32 image in Paint
2. Save as PNG
3. Use online converter: https://convertico.com/
4. Convert PNG to ICO
5. Download and place in Resources folder

### Using Icon Editor
Download a free icon editor:
- IcoFX: http://icofx.ro/
- Greenfish Icon Editor: http://greenfishsoftware.org/

Or use online tools:
- https://www.icoconverter.com/
- https://convertio.co/png-ico/
- https://favicon.io/

## Current Status

? Application starts without crashing (with or without icon)
? Falls back to default system icon if file missing
? Searches multiple locations for icon file
? Logs debug messages about icon loading
? Icon is optional - not required for operation
