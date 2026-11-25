using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Win32.UI.XamlHost;
using Windows.UI.Xaml.Hosting;

namespace GanttIsland
{
    // COM interface that lets us attach the XAML island to a native window handle (HWND).
    [ComImport, Guid("3cbcf1bf-2f76-4e9c-96ab-e84b37972554"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWindowXamlSourceNative
    {
        void AttachToWindow(IntPtr parentWindowHandle);
        IntPtr WindowHandle { get; }
    }

    // =============================================================================================
    // Bridge - the doorway between the native C++ application (GanttHost.exe) and the UWP Gantt.
    //
    // BIG PICTURE
    //   GanttHost.exe (Win32 C++)
    //        |  starts the .NET runtime and calls the public static methods below by name
    //        v
    //   Bridge (this file)  ->  GanttPage (builds the Gantt, binds data, subscribes events)
    //        |  XAML Islands (DesktopWindowXamlSource) hosts the UWP content inside the Win32 window
    //        v
    //   SfGantt (Syncfusion UWP control) drawn inside the C++ window
    //
    // EXECUTION SEQUENCE
    //   1. The C++ app creates its main window.
    //   2. The C++ app calls Initialize   -> the Gantt is created and shown.
    //   3. The window is resized          -> the C++ app calls Resize.
    //   4. The window is closed           -> the C++ app calls Shutdown.
    //
    // The C++ side can only call methods with the exact shape:  public static int MethodName(string argument).
    // Every call arrives on the native UI (STA) thread, which is the thread XAML must be used from.
    // =============================================================================================
    public static class Bridge
    {
        // Flags for SetWindowPos: keep z-order, do not steal focus, make the window visible.
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        // Keeps the UWP framework alive for as long as the app runs.
        private static GanttXamlApp xamlApplication;

        // The XAML island: a child window inside the C++ window that renders UWP content.
        private static DesktopWindowXamlSource xamlIslandSource;

        // The page that owns the Gantt; LoadTasks and SetEventCallback work through it.
        private static GanttPage currentPage;

        // The window handle of the island, needed to move and resize it.
        private static IntPtr islandWindowHandle;

        // ---------------------------------------------------------------------------------
        // STEP 1 - Initialize: called once by the C++ app after its main window exists.
        // argument format: "<parent window handle>;<x>;<y>;<width>;<height>"
        // Returns 0 on success, otherwise an error code (details are written to GanttIsland.log).
        // ---------------------------------------------------------------------------------
        public static int Initialize(string argument)
        {
            // Syncfusion's UWP assemblies reference System.Runtime 4.2.1.0, which .NET Framework does not
            // have. build.cmd generates it as System.Runtime.facade.bin. It is loaded from bytes here
            // because a delay-signed file on disk fails strong-name validation (0x8013141A) on some PCs.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                AssemblyName requestedAssembly = new AssemblyName(resolveArgs.Name);
                if (requestedAssembly.Name != "System.Runtime") return null;

                string bridgeFolder = Path.GetDirectoryName(typeof(Bridge).Assembly.Location);
                return Assembly.Load(File.ReadAllBytes(Path.Combine(bridgeFolder, "System.Runtime.facade.bin")));
            };

            try
            {
                return InitializeCore(argument);
            }
            catch (Exception exception)
            {
                // A missing assembly is reported here, when InitializeCore is compiled, not inside it.
                WriteLog(exception.ToString());
                return exception.HResult != 0 ? exception.HResult : -1;
            }
        }

        // The real start-up work. It is a separate method so that Initialize can catch assembly load
        // errors; NoInlining keeps the two methods from being merged by the JIT compiler.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int InitializeCore(string argument)
        {
            WriteLog("Initialize: " + argument);

            string[] argumentParts = argument.Split(';');
            IntPtr parentWindowHandle = new IntPtr(long.Parse(argumentParts[0], CultureInfo.InvariantCulture));

            // 1. Start the UWP XAML framework on this thread (must happen before any UWP control exists).
            xamlApplication = new GanttXamlApp();

            // 2. Create the XAML island and attach it as a child of the C++ window.
            xamlIslandSource = new DesktopWindowXamlSource();
            IDesktopWindowXamlSourceNative islandNative = (IDesktopWindowXamlSourceNative)(object)xamlIslandSource;
            islandNative.AttachToWindow(parentWindowHandle);
            islandWindowHandle = islandNative.WindowHandle;

            // 3. Register the Syncfusion license BEFORE the first Syncfusion control is created.
            //    The key is read from an environment variable so it never ends up in source control.
            //    Without a key the control still works and shows a trial-license dialog.
            string licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
            if (!string.IsNullOrWhiteSpace(licenseKey))
                Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licenseKey);

            // 4. Build the page. Inside the GanttPage constructor the data is created, bound to the
            //    Gantt and the events are subscribed (see GanttPage.cs).
            GanttPage ganttPage = new GanttPage();
            currentPage = ganttPage;

            // 5. Show the page in the island. From here the Gantt loads and renders the tasks.
            xamlIslandSource.Content = ganttPage.RootLayout;

            // 6. Position the island inside the C++ window.
            string boundsArgument = argument.Substring(argument.IndexOf(';') + 1);
            return Resize(boundsArgument);
        }

        // ---------------------------------------------------------------------------------
        // STEP 2 - Resize: called by the C++ app whenever its window size changes.
        // argument format: "<x>;<y>;<width>;<height>"
        // ---------------------------------------------------------------------------------
        public static int Resize(string argument)
        {
            try
            {
                if (islandWindowHandle == IntPtr.Zero) return 0;

                string[] boundsParts = argument.Split(';');
                int x = int.Parse(boundsParts[0], CultureInfo.InvariantCulture);
                int y = int.Parse(boundsParts[1], CultureInfo.InvariantCulture);
                int width = int.Parse(boundsParts[2], CultureInfo.InvariantCulture);
                int height = int.Parse(boundsParts[3], CultureInfo.InvariantCulture);

                SetWindowPos(islandWindowHandle, IntPtr.Zero, x, y, width, height,
                             SwpNoZOrder | SwpNoActivate | SwpShowWindow);
                return 0;
            }
            catch (Exception exception)
            {
                WriteLog(exception.ToString());
                return -1;
            }
        }

        // ---------------------------------------------------------------------------------
        // C++ -> GANTT (data): called by the C++ app to send tasks to the chart.
        // argument: one task per line, "id|name|yyyy-MM-dd|yyyy-MM-dd|progress"
        // Returns the number of tasks added, or -1 on error.
        // ---------------------------------------------------------------------------------
        public static int LoadTasks(string argument)
        {
            try
            {
                int addedCount = currentPage == null ? -1 : currentPage.AddTasksFromText(argument);
                WriteLog("LoadTasks: added " + addedCount + " task(s)");
                return addedCount;
            }
            catch (Exception exception)
            {
                WriteLog(exception.ToString());
                return -1;
            }
        }

        // Signature of the native function the C++ app registers to receive Gantt events.
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void NativeEventCallback(string eventName, string taskId, string details);

        // Must stay in a static field so the garbage collector does not free the delegate.
        private static NativeEventCallback nativeEventCallback;

        // ---------------------------------------------------------------------------------
        // GANTT -> C++ (events): the C++ app passes the address of its callback function once.
        // argument: the function pointer as a decimal number. After this, every Gantt event is
        // forwarded to that function (see GanttPage.WriteToEventLog).
        // ---------------------------------------------------------------------------------
        public static int SetEventCallback(string argument)
        {
            try
            {
                IntPtr functionPointer = new IntPtr(long.Parse(argument, CultureInfo.InvariantCulture));
                nativeEventCallback = (NativeEventCallback)Marshal.GetDelegateForFunctionPointer(functionPointer, typeof(NativeEventCallback));
                currentPage.NativeEventForwarder = (eventName, taskId, details) => nativeEventCallback(eventName, taskId, details);
                return 0;
            }
            catch (Exception exception)
            {
                WriteLog(exception.ToString());
                return -1;
            }
        }

        // ---------------------------------------------------------------------------------
        // STEP 3 - Shutdown: called by the C++ app when its window is being destroyed.
        // Releases the XAML island so the UWP content is disposed cleanly.
        // ---------------------------------------------------------------------------------
        public static int Shutdown(string argument)
        {
            try
            {
                if (xamlIslandSource != null)
                {
                    xamlIslandSource.Content = null;
                    xamlIslandSource.Dispose();
                }
                xamlIslandSource = null;
                nativeEventCallback = null;
                currentPage = null;
                islandWindowHandle = IntPtr.Zero;
                return 0;
            }
            catch (Exception exception)
            {
                WriteLog(exception.ToString());
                return -1;
            }
        }

        // Appends a line to GanttIsland.log (next to the executable) to help diagnose start-up problems.
        private static void WriteLog(string message)
        {
            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GanttIsland.log");
                File.AppendAllText(logFilePath, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the application.
            }
        }
    }
}
