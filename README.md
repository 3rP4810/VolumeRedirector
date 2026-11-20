# Volume Redirector

## [1.1.0]

### Added
- Graphical User Interface based on **Wpf.Ui** for Windows 11 Fluent Design style.
- Native Toast Notification support via **Microsoft.Toolkit.Uwp.Notifications**.
- Structured logging using **NLog** for diagnostics and tracking.
- Documentation updates in `README.md` introducing new features and dependencies.

---

A Windows utility that redirects the media keys (**Volume Up**, **Volume Down**, and **Volume Mute**) to control the volume of a **single application** (such as Spotify) instead of the system master volume.

A system tray icon keeps the application running in the background, while a modern Windows 11-style flyout shows the targeted app, its volume level, and mute status. The user interface features a modern Fluent UI design, supports native Windows Toast notifications, and integrates advanced diagnostic logging.

> 📷 **Screenshot placeholder:** Flyout displaying application icon, name, speaker status, progress bar, and percentage.
> 📷 **Screenshot placeholder:** Fluent UI Settings Window.

---

## What It's For

**Practical Example:** You are watching a video while listening to background music on Spotify. By selecting Spotify as your target application in Volume Redirector:

- **Volume Up / Down** will adjust only Spotify's volume, leaving the video playback unaffected.
- **Volume Mute** will mute only Spotify while system audio continues playing.
- If Spotify is closed, the media keys automatically fall back to controlling the system master volume so they remain functional at all times.

---

## Features

| Feature | Description |
|---|---|
| **Fluent UI Interface** | Modern GUI built with `Wpf.Ui`, seamlessly matching Windows 11 visual elements and native Light/Dark themes. |
| **App Selection** | Choose target apps from active audio sessions (the same list found in the Windows Volume Mixer). |
| **Volume Up / Down** | Adjusts the target app volume in 5% increments per key press without modifying master volume. |
| **Volume Mute** | Toggles mute state exclusively for the selected app. Mute changes once per key press, even when held down. |
| **Windows 11-style Flyout** | Displays an overlay card with the app icon, name, volume bar, and percentage value upon key press. Adapts to Windows accent color and dark/light mode. |
| **Windows System Fallback** | Reverts key presses to default system volume behavior if no target app is selected or if the targeted app has no active audio session. |
| **Native Toast & Tray Alerts** | Toast notifications (`Microsoft.Toolkit.Uwp.Notifications`) and balloon tips alert when the target app cannot be controlled (rate-limited to 1 alert per 10 seconds). |
| **Logging System (NLog)** | Comprehensive event, error, and status tracking recorded in log files to simplify troubleshooting and debugging. |
| **Auto-start with `--run`** | Launches minimized directly to the system tray without showing a window or startup popups (designed for Windows Startup). |
| **Manual Launch** | Double-clicking the executable opens the main settings window. |
| **Single Instance** | Restricts execution to one single instance per user session. Launching a second manual instance brings the active window to the foreground. |
| **Run at Startup** | A checkbox in settings creates or removes a shortcut in the Windows Startup folder with the `--run` flag. |
| **System Tray Icon** | Double-click to open settings; includes a context menu with **Settings** and **Exit**. |

---

## Requirements

- **Windows 11.** The flyout and main user interface are designed specifically for Windows 11 (using `Wpf.Ui` and the *Segoe Fluent Icons* font). Compatibility with Windows 10 is unverified.
- **.NET 10** (`net10.0-windows` application): Requires the *.NET 10 Desktop Runtime* to execute and *.NET 10 SDK* to build.
- An active default audio playback device.

### Known Limitations

- The app monitors audio sessions on the **default playback device**.
- The flyout appears on the **primary monitor**. It may not be visible in exclusive full-screen games (borderless windowed mode is supported).
- Windows limits media key interception for non-elevated applications when an elevated (Administrator) window is in the foreground. This is a system-level restriction.
- **Headset / Bluetooth Controls**: It is not possible to prevent Bluetooth headset controls from modifying the system master volume. Unlike keyboard keys (which are intercepted and blocked by the low-level hook prior to Windows processing), headphone gestures arrive directly as device volume changes applied by Windows. There is no public API to intercept these in advance. When Absolute Volume is enabled, Windows volume and headset volume remain synchronized; disabling Absolute Volume decouples the controls, causing Windows to miss headset input entirely while requiring administrator registry modifications and system restarts.

---

## Installation

1. Install the **.NET 10 Desktop Runtime** if it is not already installed.
2. Copy the **entire folder** containing `VolumeRedirector.exe` and its dependencies (`Wpf.Ui.dll`, `Wpf.Ui.Abstractions.dll`, `NLog.dll`, `Microsoft.Toolkit.Uwp.Notifications.dll`, `NAudio` libraries, etc.) to a permanent location (e.g., `C:\Program Files\VolumeRedirector\`).
3. Launch `VolumeRedirector.exe` by double-clicking it to open the settings window.
4. *(Optional)* Check **Run at Startup** to automatically launch the app when logging in (see [Auto-start](#auto-start-with---run)).

> If you relocate the folder after enabling "Run at Startup", the shortcut will point to the previous location. Open the app from its new location and re-check the startup box.

---

## First Steps

1. Launch your desired app (e.g., Spotify) and **play audio**: the app will only appear in the list once an active audio session exists.
2. Open Volume Redirector. The **Select Target Application** window will open.
3. Select your application from the list. If it does not appear, click **🔄 Refresh List**.
4. Click **✅ Select**. A confirmation will appear, and the window will close. The app **remains active in the system tray**.
5. Press **Volume Up** or **Volume Down**: the target app's volume changes, and the flyout overlay displays.
6. Press **Volume Mute**: the target app is muted, showing a muted speaker icon on the flyout. Press again to unmute.
7. To change the targeted app or remove redirection, double-click the system tray icon.

> If the tray icon is hidden, check the overflow menu (the **^** arrow on the taskbar). You can drag the icon onto the main taskbar area.

---

## How It Works

### Key Behaviors

An "audio session" refers to any app registered in the Windows Volume Mixer.

| Condition | Volume Up / Down | Volume Mute | Flyout | Notification / Alert |
|---|---|---|---|---|
| **No app selected** | System volume | System mute | No | No |
| **Selected app closed** | System volume | System mute | No | "*App Name* is not running" |
| **Selected app open, no active audio** | System volume | System mute | No | "*App Name* is open but not playing audio, using system volume" |
| **Selected app with active audio session** | Adjusts app volume ±5% | Toggles app mute | Yes | No |
| **Selected app is muted** | **Up** unmutes & raises volume. **Down** lowers volume while keeping muted. | Unmutes app | Yes | No |

Additional details:

- When media keys fall back to Windows, the native **system flyout** is displayed instead of the custom overlay.
- Alerts and notifications are rate-limited to **once every 10 seconds**. Clicking an alert opens the settings window.
- The volume percentage shown in the flyout reflects the app's slider position in the **Windows Volume Mixer**, rather than the combined master output. (e.g., System volume at 30% and App volume at 100% displays 100%).

### Manual Launch

Double-clicking the executable without command-line arguments opens the settings window. If the saved target app is not running, a warning notification displays. Any argument other than `--run` is treated as a manual launch.

### Auto-start with `--run`

Passing `--run` launches the application **minimized in the system tray** without displaying the window or startup notifications.

You can configure auto-start in two ways:

**Via Settings Checkbox (Recommended):** Check **Run at Startup** inside the settings window. This creates a `VolumeRedirector.lnk` shortcut pointing to the executable with the `--run` argument in your Windows Startup folder. Unchecking the box removes the shortcut.

**Manually:** Press `Win + R`, type `shell:startup`, create a shortcut to the executable, and append `--run` in the *Target* path field:

```text
"C:\Program Files\VolumeRedirector\VolumeRedirector.exe" --run
```

### Single Instance Management

Only one instance of Volume Redirector can run per user session:

- Launching a **second manual instance** brings the active window to the foreground and exits.
- Launching a **second `--run` instance** exits silently.

### Exiting the Application

- Closing the settings window does **not** exit the application; it continues running in the system tray.
- To quit the app completely, right-click the system tray icon and select **Exit**.

### Persistent Mute State

Volume Redirector **never automatically unmutes an app** upon exit, closure, target removal, or app swapping. If Spotify is muted and you exit Volume Redirector, Spotify remains muted. You can unmute it via **Volume Up** while the program is running or through the **Windows Volume Mixer**.

---

## Settings, Files, & Diagnostics

### Configuration File

Settings are saved in JSON format at:

```text
%APPDATA%\VolumeRedirector\settings.json
```

Example configuration:

```json
{
  "TargetProcessName": "spotify",
  "TargetDisplayName": "Spotify",
  "VolumeStep": 0.05,
  "MinimizeToTray": false
}
```

| Field | Description |
|---|---|
| `TargetProcessName` | Process name of the targeted application (e.g., `spotify`, `chrome`). Empty if no app is selected. |
| `TargetDisplayName` | Friendly display name shown in flyouts and notifications. |
| `VolumeStep` | Volume step per key press, between `0.0` and `1.0`. Default is `0.05` (5%). Resets to `0.05` when selecting or removing an app. |

If the configuration file is missing or corrupted, default settings are applied (no app selected).

### Diagnostic Logging (NLog)

Log files generated by **NLog** are stored in the application data directory to assist in diagnosing runtime errors, hotkey interception, and audio session management:

```text
%APPDATA%\VolumeRedirector\logs\
```

### Startup Shortcut

Created when **Run at Startup** is enabled:

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\VolumeRedirector.lnk
```

---

## Troubleshooting

| Problem | Cause | Solution |
|---|---|---|
| Volume keys always modify system volume | No target app selected, or Volume Redirector is not running | Locate the icon in the system tray. Select a target app and click **Select**. |
| Volume keys affect system volume and show a notification | Target app is closed or has no active audio stream | Start the application and begin audio playback. |
| Target app is missing from the list | No active audio session detected | Start playback in the app and click **🔄 Refresh List**. |
| App is muted unexpectedly | **Volume Mute** was toggled | Press **Volume Up** or **Volume Mute**, or adjust it in the Windows Volume Mixer. |
| System tray icon is missing | Windows hid the icon in the overflow menu | Click the **^** arrow in the taskbar and drag the icon into view. |
| Application icon shows default image | Icon resource failed to load | Ensure icon resources are correctly bundled during compilation. |
| Flyout overlay does not appear | Keys fell back to system, primary monitor is inactive, or a game is running in exclusive full-screen mode | Switch game settings to borderless windowed mode. |
| Notifications are not appearing | Rate-limited (1 per 10s) or Windows "Do Not Disturb" / Focus Assist is active | Check the Windows Notification Center settings. |
| "Run at Startup" checkbox unchecks itself | Executable was moved or shortcut was deleted | Re-open the app from its permanent location and re-enable startup. |
| Startup shortcut creation failed | Startup folder is read-only or lacks permissions | Create the shortcut manually in `shell:startup`. |

---

## Project Structure & Dependencies

### Main Dependencies

- **`Wpf.Ui` / `Wpf.Ui.Abstractions`**: Modern Windows 11 Fluent UI framework.
- **`Microsoft.Toolkit.Uwp.Notifications`**: Windows native Toast notification library.
- **`NLog`**: Structured logging framework.
- **`NAudio` (`NAudio.Wasapi`, `NAudio.Core`, etc.)**: Windows WASAPI audio session manager.

### File Structure

| File | Role |
|---|---|
| `App.xaml.cs` | App initialization, `--run` flag handling, system tray icon, Toast notifications, hotkey routing, flyout management. |
| `MainWindow.xaml` / `.cs` | **Wpf.Ui** settings window: app list selection, **Select**, **Remove Link**, **Run at Startup**. |
| `VolumeFlyoutWindow.xaml` / `.cs` | Transparent, non-focusable overlay window displaying Fluent UI themes, volume sliders, and accent colors. |
| `Services/HotkeyService.cs` | Global low-level keyboard hook (`WH_KEYBOARD_LL`) for Volume Up, Down, and Mute. |
| `Services/AudioService.cs` | Audio session enumerator and volume/mute manager using NAudio WASAPI. |
| `Services/SettingsService.cs` | Reads and writes `settings.json` configuration files. |
| `Services/SingleInstanceService.cs` | Single-instance app mutex manager and IPC window restore trigger. |
| `Services/StartupShortcutService.cs` | Handles Windows Startup folder shortcut creation, detection, and removal. |
| `Services/NotificationService.cs` | Native Toast notification dispatcher. |
| `Models/AppSettings.cs` | Application settings data model. |