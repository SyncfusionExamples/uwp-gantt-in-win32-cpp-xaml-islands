# Syncfusion UWP Gantt in a Win32 C++ application (XAML Islands)

This sample shows how to host the Syncfusion **UWP Gantt (`SfGantt`)** control inside a native **Win32 C++** application, and answers two common questions:

- **How is data sent to the Gantt chart?**
- **How do we receive events from the Gantt chart?**

![Gantt hosted in a Win32 C++ window](shot_after.png)

## How it works

The UWP Gantt is a managed control and cannot be created directly from C++. The sample therefore has two projects:

```
GanttHost.exe  (Win32 C++)
   |  starts the .NET Framework runtime and calls methods by name
   v
GanttIsland.dll  (C#, .NET Framework 4.8)  -  Bridge.cs
   |  XAML Islands (DesktopWindowXamlSource)
   v
SfGantt  (Syncfusion UWP control, drawn inside the C++ window)
```

| Project | Language | Role |
|---|---|---|
| `GanttHost` | C++ | Creates the window, starts the .NET runtime, calls the bridge, shows the toolbar. |
| `GanttIsland` | C# | Creates the Gantt, binds the data, subscribes to events, talks to C++. |

## Sending data to the Gantt (C++ to Gantt)

1. The C++ code calls the bridge method `LoadTasks` with the tasks as text, one task per line:
   `id|name|yyyy-MM-dd|yyyy-MM-dd|progress`
2. The C# code adds the tasks to the `ObservableCollection<TaskDetail>` that is bound to `SfGantt.ItemsSource`.
3. Because the collection is an `ObservableCollection`, the grid rows and task bars appear with no refresh call.

Try it: click **Send tasks from C++**, then scroll to the bottom of the grid. The two new tasks are in `GanttHost/main.cpp` (`SendTasksToGantt`). Replace that text with your own data.

## Receiving events from the Gantt (Gantt to C++)

1. At startup, the C++ code gives the address of the function `OnGanttEvent` to the bridge by calling `SetEventCallback`.
2. The Gantt raises its events in C# (see `SubscribeToGanttEvents` in `GanttIsland/GanttPage.cs`).
3. Every event passes through `WriteToEventLog`, which also calls `OnGanttEvent` with the event name, task ID and old/new values.

Try it: select a task, or drag, resize or edit one. The label in the C++ toolbar shows the last event. The Event Logger panel on the right shows the same events from the C# side.

Events forwarded to C++: selection, cell edit, task bar drag, task bar resize, progress change, dependency creation, and task expand/collapse.

## Project structure

```
build.cmd                  Builds both projects into .\out
GanttHost\
  main.cpp                 Win32 window, .NET start-up, LoadTasks call, OnGanttEvent callback
  make-config.ps1          Generates GanttHost.exe.config, copies facade DLLs and XAML resources
  GanttHost.exe.manifest   Enables XAML Islands (Windows 10 1903+) and DPI awareness
GanttIsland\
  Bridge.cs                Methods the C++ code calls: Initialize, Resize, LoadTasks, SetEventCallback, Shutdown
  GanttPage.cs             Creates the Gantt, binds data, subscribes to events, builds the layout
  SampleData.cs            Creates the sample tasks, dependencies and resources
  GanttXamlApp.cs          Starts the UWP XAML framework and resolves Syncfusion types
```

## Requirements

- Windows 10 version 1903 (build 18362) or later
- Visual Studio 2019 or later with **Desktop development with C++**, plus the **.NET Framework 4.8** targeting pack
- .NET SDK (for `dotnet build`)
- Windows 10 SDK (for `makepri.exe`)
- Internet access on the first build, to restore NuGet packages and download Mono.Cecil
- Syncfusion license key (optional, see below)

## Build and run

1. Open a **Command Prompt** (not PowerShell) and go to the sample folder.
2. Run:
   ```
   build.cmd
   ```
3. Run `out\GanttHost.exe`.

Close the application before building again; otherwise `GanttIsland.dll` is locked and the build fails.

## Syncfusion license key

Without a key the Gantt works but shows a trial-license dialog. To remove it, set your key in an environment variable before starting the application:

```
set SYNCFUSION_LICENSE_KEY=<your key>
out\GanttHost.exe
```

The key is read in `Bridge.cs` and registered before the Gantt is created. Do not put the key in source code.

## Good to know

- A small C# .NET Framework layer is always needed between C++ and the Gantt.
- All bridge calls must run on the UI thread. In this sample they do, because they come from the window procedure and the Gantt's own events.
- `build.cmd` generates `System.Runtime.facade.bin`, a compatibility assembly that the Syncfusion UWP libraries need on .NET Framework. `Bridge.cs` loads it from bytes at start-up. Keep that file next to the executable.
- If the app shows "Creating the Gantt island failed", check `out\GanttIsland.log` for the cause.

## Troubleshooting

| Problem | Fix |
|---|---|
| Build error: file locked by `GanttHost` | Close the running application and build again. |
| Error `0x8013141A` (strong name validation) at start-up | Rebuild with `build.cmd`; make sure `System.Runtime.facade.bin` is in `out`. |
| Trial license dialog appears | Set `SYNCFUSION_LICENSE_KEY` as shown above. |
| New tasks are not visible after the button click | Scroll to the bottom of the grid; the tasks are added after the last row. |
| Window does not start on older Windows | XAML Islands needs Windows 10 1903 or later. |
