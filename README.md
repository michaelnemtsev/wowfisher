# WowFisher

WowFisher is a Windows overlay that watches a selected part of the game and presses `/` when it detects a sudden change around the fishing bobber. The floating controls stay above the game. Clicks outside the two buttons pass through, including in the gap between them.

## Requirements

- Windows and the .NET 8 Desktop Runtime. To build or run from source, install the .NET 8 SDK.
- World of Warcraft with the **ForeverFisher** addon installed and enabled. WowFisher depends on ForeverFisher for in-game casting and reeling. In World of Warcraft, bind the addon's fishing action to the main keyboard `/` key (US layout).

## Run

Use windowed or borderless windowed mode for the game. Open `WowFisher.exe` from the build output or run the project with the .NET 8 SDK.

```powershell
dotnet run --project .\WowFisher.csproj
```

1. Launch WowFisher with ForeverFisher enabled in World of Warcraft. Cast once in the game so the bobber is visible.
2. With the game in front, find the **WowFisher** icon in the Windows system tray. If the icon is hidden, click the up arrow next to the clock.
3. Right-click the **WowFisher** icon and choose **Select bobber area…**. Drag a tight rectangle around the resting bobber, keeping it near the center with a little water on all sides.
4. Return focus to the game and click **Start Fishing** in the WowFisher overlay.

**Start Fishing** presses `/` to cast. Once the bobber settles, WowFisher watches for the bite, presses `/` to reel it in, then waits two seconds before pressing `/` to cast again. The cycle repeats until you click **Stop Fishing**, which also cancels a pending cast. Monitoring and key presses pause while the game is out of focus. If the bobber lands outside the rectangle or the game window changes size, select the area again.

## Record a sample

1. Select an area that shows the resting bobber and its bite animation.
2. With the game focused, press **Ctrl+Shift+R** shortly before a bite. You can also choose **Record sample** in the tray and return to the game within 10 seconds.
3. Let the bite happen, then press **Ctrl+Shift+R** again. The recorder also saves after 12 seconds or when the game loses focus. Fishing automation stops while you record.
4. Choose **Open captures folder** from the tray. Share the `.avi` and tell me roughly when the bobber distorts. A clip with a few seconds of normal water motion before the bite will help tune the detector.

Clips contain only the selected region and are saved locally in `%LOCALAPPDATA%\WowFisher\Captures`. The detector is tuned to the brief pale splash as the bobber tilts. Leave room for the splash above and around the bobber when selecting the area.
