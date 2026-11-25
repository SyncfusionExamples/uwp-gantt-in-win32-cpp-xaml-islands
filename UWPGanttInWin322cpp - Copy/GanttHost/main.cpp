// Win32 C++ host for a Syncfusion UWP SfGantt control (XAML Islands).
//
// The Syncfusion UWP assemblies are managed (.NET) libraries, so the native app
// starts the .NET Framework 4.x CLR in-process and calls static entry points in
// GanttIsland.dll (see ..\GanttIsland\Bridge.cs). That library creates the
// DesktopWindowXamlSource, attaches it to our window and returns the island HWND.
//
// TWO-WAY COMMUNICATION DEMONSTRATED HERE
//   C++ -> Gantt : SendTasksToGantt() calls the bridge method "LoadTasks" with the task data as text.
//   Gantt -> C++ : OnGanttEvent() is registered once with "SetEventCallback"; the Gantt then calls it
//                  for every event (selection, edit, drag, resize, progress, dependency, expand/collapse).

#include <windows.h>
#include <windowsx.h>
#include <metahost.h>
#include <mscoree.h>
#include <string>
#include <wrl/client.h>

#pragma comment(lib, "mscoree.lib")

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr wchar_t kClassName[] = L"GanttHostWindow";
    constexpr int kToolbarHeight = 40;
    constexpr int kSendTasksButtonId = 1001;

    ComPtr<ICLRRuntimeHost> g_clr;
    std::wstring g_bridgeDll;
    HWND g_hwnd = nullptr;
    HWND g_sendTasksButton = nullptr;
    HWND g_eventLabel = nullptr;   // shows the last Gantt event received in C++
    bool g_ready = false;

    std::wstring ExeDirectory()
    {
        wchar_t path[MAX_PATH];
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        std::wstring s(path);
        return s.substr(0, s.find_last_of(L'\\') + 1);
    }

    bool StartClr()
    {
        ComPtr<ICLRMetaHost> meta;
        if (FAILED(CLRCreateInstance(CLSID_CLRMetaHost, IID_PPV_ARGS(&meta)))) return false;

        ComPtr<ICLRRuntimeInfo> info;
        if (FAILED(meta->GetRuntime(L"v4.0.30319", IID_PPV_ARGS(&info)))) return false;

        if (FAILED(info->GetInterface(CLSID_CLRRuntimeHost, IID_PPV_ARGS(&g_clr)))) return false;
        if (FAILED(g_clr->Start())) return false;
        return true;
    }

    // Calls: public static int <method>(string arg) in GanttIsland.Bridge.
    DWORD CallBridge(const wchar_t* method, const std::wstring& arg)
    {
        DWORD ret = static_cast<DWORD>(-1);
        HRESULT hr = g_clr->ExecuteInDefaultAppDomain(
            g_bridgeDll.c_str(), L"GanttIsland.Bridge", method, arg.c_str(), &ret);
        return FAILED(hr) ? static_cast<DWORD>(hr) : ret;
    }

    std::wstring RectArg(HWND hwnd)
    {
        RECT rc;
        GetClientRect(hwnd, &rc);
        return std::to_wstring(0) + L";" + std::to_wstring(kToolbarHeight) + L";" +
               std::to_wstring(rc.right) + L";" + std::to_wstring(max(0L, rc.bottom - kToolbarHeight));
    }

    void ShowError(HWND hwnd, const wchar_t* what, DWORD code)
    {
        wchar_t msg[512];
        swprintf_s(msg, L"%s failed (0x%08X).\nSee GanttIsland.log next to the executable for details.", what, code);
        FILE* f = nullptr;
        if (_wfopen_s(&f, (ExeDirectory() + L"GanttHost.log").c_str(), L"a") == 0 && f)
        {
            fwprintf(f, L"%s\n", msg);
            fclose(f);
        }
        MessageBoxW(hwnd, msg, L"GanttHost", MB_ICONERROR);
    }

    // GANTT -> C++ : receives every event the Gantt raises.
    // Its address is given to the managed side once (SetEventCallback, in wWinMain).
    // It is called on the UI thread, so it may touch windows directly.
    void __stdcall OnGanttEvent(const wchar_t* eventName, const wchar_t* taskId, const wchar_t* details)
    {
        std::wstring text = L"Gantt event: " + std::wstring(eventName) +
                            L"   |   Task ID: " + (*taskId ? taskId : L"-") +
                            (*details ? L"   |   " + std::wstring(details) : L"");
        SetWindowTextW(g_eventLabel, text.c_str());
        OutputDebugStringW((text + L"\n").c_str());   // also visible in the debugger Output window
    }

    // C++ -> GANTT : sends new tasks to the chart. One task per line:
    // "id|name|yyyy-MM-dd|yyyy-MM-dd|progress". The chart shows them immediately.
    // Task IDs must be unique, so the button only sends the tasks once.
    void SendTasksToGantt()
    {
        static bool alreadySent = false;
        if (alreadySent) return;

        const std::wstring tasks =
            L"101|Task sent from C++ 1|2026-11-23|2026-11-25|0\n"
            L"102|Task sent from C++ 2|2026-11-25|2026-11-27|40\n";
        if (CallBridge(L"LoadTasks", tasks) == 2)
        {
            alreadySent = true;
            EnableWindow(g_sendTasksButton, FALSE);
        }
    }

    LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
    {
        switch (msg)
        {
        case WM_CREATE:
        {
            HGDIOBJ font = GetStockObject(DEFAULT_GUI_FONT);
            g_sendTasksButton = CreateWindowExW(0, L"BUTTON", L"Send tasks from C++", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                8, 6, 160, 28, hwnd, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kSendTasksButtonId)),
                GetModuleHandleW(nullptr), nullptr);
            g_eventLabel = CreateWindowExW(0, L"STATIC", L"Gantt event: (none yet - select, drag or edit a task)",
                WS_CHILD | WS_VISIBLE | SS_NOPREFIX | SS_CENTERIMAGE,
                180, 6, 1000, 28, hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
            SendMessageW(g_sendTasksButton, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
            SendMessageW(g_eventLabel, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
            return 0;
        }
        case WM_COMMAND:
            if (LOWORD(wp) == kSendTasksButtonId && g_ready) SendTasksToGantt();
            return 0;
        case WM_SIZE:
            if (g_ready) CallBridge(L"Resize", RectArg(hwnd));
            return 0;
        case WM_DESTROY:
            if (g_ready) CallBridge(L"Shutdown", L"");
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProcW(hwnd, msg, wp, lp);
    }
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, PWSTR, int nShow)
{
    // XAML Islands need a single-threaded apartment on the UI thread.
    if (FAILED(OleInitialize(nullptr))) return 1;

    WNDCLASSEXW wc{ sizeof(wc) };
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    wc.lpszClassName = kClassName;
    RegisterClassExW(&wc);

    g_hwnd = CreateWindowExW(0, kClassName, L"Win32 C++ host - Syncfusion UWP Gantt (XAML Islands)",
        WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, CW_USEDEFAULT, CW_USEDEFAULT, 1280, 720,
        nullptr, nullptr, hInst, nullptr);
    if (!g_hwnd) return 1;

    ShowWindow(g_hwnd, nShow);
    UpdateWindow(g_hwnd);

    g_bridgeDll = ExeDirectory() + L"GanttIsland.dll";
    if (!StartClr())
    {
        MessageBoxW(g_hwnd, L"Could not start the .NET Framework 4.x runtime.", L"GanttHost", MB_ICONERROR);
        return 1;
    }

    std::wstring arg = std::to_wstring(reinterpret_cast<INT_PTR>(g_hwnd)) + L";" + RectArg(g_hwnd);
    DWORD rc = CallBridge(L"Initialize", arg);
    if (rc != 0)
    {
        ShowError(g_hwnd, L"Creating the Gantt island", rc);
    }
    else
    {
        g_ready = true;
        // Register OnGanttEvent so the Gantt can call back into this application.
        CallBridge(L"SetEventCallback", std::to_wstring(reinterpret_cast<INT_PTR>(&OnGanttEvent)));
    }

    MSG m;
    while (GetMessageW(&m, nullptr, 0, 0))
    {
        TranslateMessage(&m);
        DispatchMessageW(&m);
    }

    OleUninitialize();
    return 0;
}
