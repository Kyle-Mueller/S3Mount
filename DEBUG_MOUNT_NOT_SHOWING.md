# Debugging: Mount Not Showing in List

## Problem
After configuring a mount, it doesn't appear in the main window list.

## Debug Logging Added

I've added comprehensive debug logging throughout the save and load process. When you run the application in Debug mode in Visual Studio, you'll see detailed output in the **Output** window.

## How to Debug

### 1. Open Output Window
- In Visual Studio, go to `View` ? `Output` (or press `Ctrl+Alt+O`)
- Make sure the dropdown says "Debug" or "Show output from: Debug"

### 2. Run the Application
- Press `F5` to run in Debug mode
- The Output window will show debug messages

### 3. Create a Mount
- Click "Add Mount"
- Fill in all the fields
- Click "Save"

### 4. Check Debug Output

You should see messages like this:

```
Save command started
Saving config: My S3 Mount, ID: 12345678-1234-1234-1234-123456789012
SaveConfiguration - Starting for My S3 Mount (ID: 12345678-...)
SaveConfiguration - Current config count: 0
SaveConfiguration - After removing duplicates: 0
SaveConfiguration - After adding new config: 1
SaveConfiguration - Serialized JSON length: 450
SaveConfiguration - Encrypted data length: 680
SaveConfiguration - Saved to credential manager
Configuration saved successfully
DialogResult set to: True
SaveButton_Click - After execute, DialogResult: True
SaveButton_Click - Setting Window DialogResult to true
AddMountAsync - Dialog result: True
AddMountAsync - Calling LoadMounts
LoadMounts - Starting
GetAllConfigurations - Starting
GetAllConfigurations - Got encrypted data, length: 680
GetAllConfigurations - Decrypted JSON length: 450
GetAllConfigurations - Returning 1 configurations
LoadMounts - Got 1 configurations
LoadMounts - Adding: My S3 Mount (ID: 12345678-...)
LoadMounts - Total mounts in collection: 1
```

## What Each Message Means

### Save Flow
1. **"Save command started"** - Save button clicked
2. **"Saving config: ..."** - Validation passed, creating config
3. **"SaveConfiguration - Starting"** - CredentialService received config
4. **"Current config count: X"** - How many configs existed before
5. **"After adding new config: X"** - How many after adding
6. **"Saved to credential manager"** - Successfully saved
7. **"Configuration saved successfully"** - No errors
8. **"DialogResult set to: True"** - ViewModel marked success
9. **"SaveButton_Click - Setting Window DialogResult to true"** - Dialog will close with success

### Load Flow
1. **"AddMountAsync - Dialog result: True"** - Dialog closed successfully
2. **"AddMountAsync - Calling LoadMounts"** - Refreshing list
3. **"LoadMounts - Starting"** - Load process started
4. **"GetAllConfigurations - Returning X configurations"** - Found configs
5. **"LoadMounts - Adding: ..."** - Adding each mount to list
6. **"LoadMounts - Total mounts in collection: X"** - Final count

## Common Issues and What to Look For

### Issue 1: Save Never Completes
**Symptoms:**
```
Save command started
[No further messages]
```
**Cause:** Validation failed (missing required field)
**Solution:** Check that all required fields are filled

### Issue 2: DialogResult Stays False
**Symptoms:**
```
DialogResult set to: False
SaveButton_Click - ViewModel DialogResult was false, not closing
```
**Cause:** Save operation threw an exception
**Solution:** Look for error message before this line

### Issue 3: Dialog Returns False
**Symptoms:**
```
AddMountAsync - Dialog result: False
AddMountAsync - Dialog was cancelled or returned false
```
**Cause:** Either you clicked Cancel, or the save failed
**Solution:** Check earlier messages for errors

### Issue 4: GetAllConfigurations Returns Empty
**Symptoms:**
```
GetAllConfigurations - No encrypted data found
GetAllConfigurations - Returning 0 configurations
```
**Cause:** Data wasn't saved to Credential Manager
**Solution:** Check if "Saved to credential manager" message appeared

### Issue 5: Configurations Found But Not Added
**Symptoms:**
```
GetAllConfigurations - Returning 2 configurations
LoadMounts - Got 2 configurations
LoadMounts - Total mounts in collection: 0
```
**Cause:** Exception during foreach loop
**Solution:** Check for error messages in between

## Manual Verification

### Check Windows Credential Manager
1. Press `Win+R` and type: `control /name Microsoft.CredentialManager`
2. Click "Windows Credentials"
3. Look for entry: "S3Mount_Configuration"
4. If it exists, the data was saved

### Check Configuration Data
If you see the credential but mounts don't load:
1. The data might be corrupted
2. Try deleting the credential and creating a new mount
3. Check the Output window for deserialization errors

## Quick Fixes

### If Mount Doesn't Appear

**Option 1: Restart Application**
```
1. Close the application completely
2. Restart in Debug mode
3. The mounts should load from Credential Manager
```

**Option 2: Check Credential Manager**
```
1. Win+R ? control /name Microsoft.CredentialManager
2. Look for "S3Mount_Configuration"
3. If missing, data wasn't saved
4. If present, data saved but not loading
```

**Option 3: Manual Refresh**
```
1. Close the configuration dialog
2. Close and reopen the main window
3. Mounts should reload
```

## Testing Checklist

Run through this checklist with debug output open:

- [ ] Click "Add Mount"
- [ ] Fill in all required fields:
  - [ ] Provider selected
  - [ ] Mount name entered
  - [ ] Bucket name entered
  - [ ] Service URL filled (auto from provider)
  - [ ] Region filled
  - [ ] Access key entered
  - [ ] Secret key entered
- [ ] Click "Test Connection" (optional but recommended)
- [ ] Click "Save"
- [ ] Check Output window for "Configuration saved successfully"
- [ ] Check Output window for "AddMountAsync - Dialog result: True"
- [ ] Check Output window for "LoadMounts - Total mounts in collection: 1"
- [ ] Check main window - mount should be visible

## Expected Output for Successful Save

```
Save command started
Saving config: Test Mount, ID: <guid>
SaveConfiguration - Starting for Test Mount (ID: <guid>)
SaveConfiguration - Current config count: 0
SaveConfiguration - After removing duplicates: 0
SaveConfiguration - After adding new config: 1
SaveConfiguration - Serialized JSON length: <number>
SaveConfiguration - Encrypted data length: <number>
SaveConfiguration - Saved to credential manager
Configuration saved successfully
DialogResult set to: True
SaveButton_Click - Before check, DialogResult: False
SaveButton_Click - After execute, DialogResult: True
SaveButton_Click - Setting Window DialogResult to true
AddMountAsync - Dialog result: True
AddMountAsync - Calling LoadMounts
LoadMounts - Starting
GetAllConfigurations - Starting
GetAllConfigurations - Got encrypted data, length: <number>
GetAllConfigurations - Decrypted JSON length: <number>
GetAllConfigurations - Returning 1 configurations
LoadMounts - Got 1 configurations
LoadMounts - Adding: Test Mount (ID: <guid>)
LoadMounts - Total mounts in collection: 1
```

## Next Steps

1. **Run the app in Debug mode** (F5 in Visual Studio)
2. **Try to create a mount**
3. **Copy the Output window contents** and share them if the issue persists
4. The debug messages will show exactly where the process is failing

## Removing Debug Logging

Once the issue is resolved, you can remove the debug logging by:
1. Searching for `System.Diagnostics.Debug.WriteLine` in the solution
2. Removing those lines
3. Or comment them out for future debugging

The logging doesn't affect performance significantly, so you can leave it in for now.
