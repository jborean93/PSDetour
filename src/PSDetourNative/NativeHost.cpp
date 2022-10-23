#include <iostream>
#include <filesystem>
#include <fstream>
#include <string>
#include <assert.h>

#include <coreclr_delegates.h>
#include <hostfxr.h>

#include <Windows.h>

// https://github.com/dotnet/samples/tree/main/core/hosting/src/NativeHost

struct worker_args_t
{
    const void *pipe;
    const wchar_t *powershell_dir;
    const wchar_t *assembly_path;
    const wchar_t *runtime_config_path;
};

namespace
{
    // Globals to hold hostfxr exports
    hostfxr_initialize_for_runtime_config_fn init_fptr;
    hostfxr_get_runtime_delegate_fn get_delegate_fptr;
    hostfxr_set_error_writer_fn set_error_fptr;
    hostfxr_close_fn close_fptr;

    // Forward declarations
    bool load_hostfxr(const char_t *);
    // load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *assembly);
    void hostfxr_error_handler(const char_t *message);
    void write_error(HANDLE pipe, const char_t *message);
}

extern "C" __declspec(dllexport) int inject(worker_args_t *p_worker_args)
{
    //
    // STEP 1: Load HostFxr and get exported hosting functions
    //
    std::filesystem::path hostfxr_path{p_worker_args->powershell_dir};
    // std::filesystem::path hostfxr_path{L"C:\\Program Files\\dotnet\\host\\fxr\\7.0.0-rc.2.22472.3"};
    hostfxr_path /= L"hostfxr.dll";
    if (!load_hostfxr(hostfxr_path.c_str()))
    {
        write_error((HANDLE)p_worker_args->pipe, L"load_hostfxr() failed");
        return 1;
    }

    //
    // STEP 2: Initialize and start the .NET Core runtime
    //
    // hostfxr_initialize_parameters init_parameters{
    //     sizeof(hostfxr_initialize_parameters),
    //     L"C:\\Program Files\\PowerShell\\7.3.0\\pwsh.exe",
    //     L"C:\\Program Files\\PowerShell\\7.3.0"};
    std::filesystem::path config_path{p_worker_args->runtime_config_path};

    hostfxr_handle hostfxr_cxt = nullptr;
    int rc = init_fptr(config_path.c_str(), nullptr, &hostfxr_cxt);
    if ((rc != 0 && rc != 1 && rc != 2) || hostfxr_cxt == nullptr)
    {
        std::wstring err_msg = L"hostfxr_initialize_for_runtime_config() failed with ";
        err_msg += std::to_wstring(rc);
        write_error((HANDLE)p_worker_args->pipe, err_msg.c_str());
        return 1;
    }

    // Get the load assembly function pointer
    load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = nullptr;
    rc = get_delegate_fptr(
        hostfxr_cxt,
        hdt_load_assembly_and_get_function_pointer,
        (void **)&load_assembly_and_get_function_pointer);
    close_fptr(hostfxr_cxt);

    if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
    {
        std::wstring err_msg = L"hostfxr_get_runtime_delegate() failed with ";
        err_msg += std::to_wstring(rc);
        write_error((HANDLE)p_worker_args->pipe, err_msg.c_str());
        return 1;
    }

    set_error_fptr(hostfxr_error_handler);

    //
    // STEP 3: Load managed assembly and get function pointer to a managed method
    //
    std::filesystem::path dotnetlib_path{p_worker_args->assembly_path};

    typedef void(CORECLR_DELEGATE_CALLTYPE * custom_entry_point_fn)(worker_args_t args);
    custom_entry_point_fn dotnet_main = nullptr;
    rc = load_assembly_and_get_function_pointer(
        dotnetlib_path.c_str(),
        L"PSDetour.RemoteWorker, PSDetour",
        L"Main",
        UNMANAGEDCALLERSONLY_METHOD,
        nullptr,
        (void **)&dotnet_main);
    if (rc != 0 || dotnet_main == nullptr)
    {
        std::wstring err_msg = L"load_assembly_and_get_function_pointer() failed with ";
        err_msg += std::to_wstring(rc);
        write_error((HANDLE)p_worker_args->pipe, err_msg.c_str());
        return 1;
    }

    worker_args_t args{
        p_worker_args->pipe,
        p_worker_args->powershell_dir};
    try
    {
        dotnet_main(args);
    }
    catch (...)
    {
        // FUTURE: Figure out how to get the exception message to return back
        write_error((HANDLE)p_worker_args->pipe, L"Unknown dotnet hosting error");

        return 1;
    }

    return 0;
}

/********************************************************************************************
 * Function used to load and activate .NET Core
 ********************************************************************************************/

namespace
{
    void *load_library(const char_t *path)
    {
        HMODULE h = ::LoadLibraryW(path);
        assert(h != nullptr);
        return (void *)h;
    }
    void *get_export(void *h, const char *name)
    {
        void *f = ::GetProcAddress((HMODULE)h, name);
        assert(f != nullptr);
        return f;
    }

    // Using the nethost library, discover the location of hostfxr and get exports
    bool load_hostfxr(const char_t *hostfxr_path)
    {
        // Pre-allocate a large buffer for the path to hostfxr
        char_t buffer[MAX_PATH];
        size_t buffer_size = sizeof(buffer) / sizeof(char_t);

        // Load hostfxr and get desired exports
        void *lib = load_library(hostfxr_path);
        init_fptr = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
        get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
        set_error_fptr = (hostfxr_set_error_writer_fn)get_export(lib, "hostfxr_set_error_writer");
        close_fptr = (hostfxr_close_fn)get_export(lib, "hostfxr_close");

        return (init_fptr && get_delegate_fptr && set_error_fptr && close_fptr);
    }

    // Load and initialize .NET Core and get desired function pointer for scenario
    // load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path)
    // {
    //     // Load .NET Core
    //     void *load_assembly_and_get_function_pointer = nullptr;
    //     hostfxr_handle cxt = nullptr;
    //     int rc = init_fptr(config_path, nullptr, &cxt);
    //     if ((rc != 0 && rc != 1 && rc != 2) || cxt == nullptr)
    //     {
    //         close_fptr(cxt);
    //         return nullptr;
    //     }

    //     // Get the load assembly function pointer
    //     rc = get_delegate_fptr(
    //         cxt,
    //         hdt_load_assembly_and_get_function_pointer,
    //         &load_assembly_and_get_function_pointer);
    //     if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
    //     {
    //         close_fptr(cxt);
    //         return nullptr;
    //     }

    //     close_fptr(cxt);
    //     return (load_assembly_and_get_function_pointer_fn)load_assembly_and_get_function_pointer;
    // }

    void hostfxr_error_handler(const char_t *message)
    {
        return;
    }

    void write_error(HANDLE pipe, const char_t *message)
    {
        int len = wcslen(message) * sizeof(wchar_t);
        byte lenBytes[5];
        lenBytes[0] = 1; // Marks this as an error
        lenBytes[1] = len & 0x000000FF;
        lenBytes[2] = (len & 0x0000FF00) >> 8;
        lenBytes[3] = (len & 0x00FF0000) >> 16;
        lenBytes[4] = (len & 0xFF000000) >> 24;

        DWORD bytesWritten;
        ::WriteFile(
            pipe,
            &lenBytes[0],
            5,
            &bytesWritten,
            NULL);

        ::WriteFile(
            pipe,
            message,
            len,
            &bytesWritten,
            NULL);
    }
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD dwReason, LPVOID lpReserved)
{
    return TRUE;
}
