# qUpdater

Automatically checks for qBittorrent updates on system startup and helps you install them fast.

The application will:
- Run automatically on system startup
- Show in the system tray
- Check for updates when started
- Won't show if there is no update available
- Download directly the last release files
  
## Install:

1. Unzip the zip from releases page
2. Run `install.bat` as administrator
3. Enjoy

## Uninstall
1. Right-click the tray icon and select "Exit"
2. Delete the program folder (in Program Files\qUpdater)
3. Remove from startup:
   - Open Registry Editor (regedit)
   - Navigate to HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
   - Delete the "qUpdater" entry
