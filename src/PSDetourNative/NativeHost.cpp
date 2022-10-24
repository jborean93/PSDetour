#include <iostream>
#include <filesystem>
#include <fstream>
#include <string>
#include <assert.h>

#include <coreclr_delegates.h>
#include <hostfxr.h>

#include <Windows.h>

struct worker_args_t
{
    const void *pipe;
    const wchar_t *powershell_dir;
    const wchar_t *assembly_path;
};

void write_error(HANDLE pipe, int result, const char_t *message)
{
    int len = wcslen(message) * sizeof(wchar_t);
    byte prefix[8];

    // First 4 bytes is the result, next 4 is the message length
    prefix[0] = result & 0x000000FF;
    prefix[1] = (result & 0x0000FF00) >> 8;
    prefix[2] = (result & 0x00FF0000) >> 16;
    prefix[3] = (result & 0xFF000000) >> 24;

    prefix[4] = len & 0x000000FF;
    prefix[5] = (len & 0x0000FF00) >> 8;
    prefix[6] = (len & 0x00FF0000) >> 16;
    prefix[7] = (len & 0xFF000000) >> 24;

    DWORD bytesWritten;
    ::WriteFile(
        pipe,
        &prefix[0],
        8,
        &bytesWritten,
        NULL);

    ::WriteFile(
        pipe,
        message,
        len,
        &bytesWritten,
        NULL);
}

void *try_load_func(HMODULE hostfxr, const char *name, HANDLE pipe)
{
    void *init_cmdline_fptr = ::GetProcAddress(hostfxr, name);
    if (init_cmdline_fptr == nullptr)
    {
        int rc = ::GetLastError();

        std::wstring err_msg = L"GetProcAddress() failed to find ";

        const size_t size = std::strlen(name) + 1;
        std::wstring w_name;
        if (size > 0)
        {
            w_name.resize(size);
            size_t out_size;
            mbstowcs_s(&out_size, &w_name[0], size, name, size - 1);
        }

        err_msg += w_name;
        write_error(pipe, rc, err_msg.c_str());
    }

    return init_cmdline_fptr;
}

extern "C" __declspec(dllexport) int inject(worker_args_t *p_worker_args)
{
    int rc = -1;
    HANDLE pipe = (HANDLE)p_worker_args->pipe;

    HMODULE hostfxr = nullptr;
    hostfxr_handle hostfxr_ctx = nullptr;
    hostfxr_close_fn close_fptr = nullptr;

    std::filesystem::path hostfxr_path{p_worker_args->powershell_dir};
    hostfxr_path /= L"hostfxr.dll";

    std::filesystem::path pwsh_dll_path{p_worker_args->powershell_dir};
    pwsh_dll_path /= "pwsh.dll";

    std::filesystem::path dotnetlib_path{p_worker_args->assembly_path};

    worker_args_t args{
        p_worker_args->pipe,
        p_worker_args->powershell_dir};

    //
    // STEP 1: Load HostFxr and get exported hosting functions
    //
    hostfxr = ::LoadLibraryW(hostfxr_path.c_str());
    if (hostfxr == nullptr)
    {
        rc = ::GetLastError();

        std::wstring err_msg = L"LoadLibraryW() failed to load ";
        err_msg += hostfxr_path.c_str();
        write_error(pipe, rc, err_msg.c_str());

        goto cleanup;
    }

    auto init_cmdline_fptr = (hostfxr_initialize_for_dotnet_command_line_fn)try_load_func(
        hostfxr, "hostfxr_initialize_for_dotnet_command_line", pipe);
    if (init_cmdline_fptr == nullptr)
    {
        rc = 1;
        goto cleanup;
    }

    auto get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)try_load_func(
        hostfxr, "hostfxr_get_runtime_delegate", pipe);
    if (init_cmdline_fptr == nullptr)
    {
        rc = 1;
        goto cleanup;
    }

    close_fptr = (hostfxr_close_fn)try_load_func(
        hostfxr, "hostfxr_close", pipe);
    if (init_cmdline_fptr == nullptr)
    {
        rc = 1;
        goto cleanup;
    }

    //
    // STEP 2: Initialize and start the .NET Core runtime
    //
    const char_t *argv = pwsh_dll_path.c_str();
    rc = init_cmdline_fptr(1, &argv, nullptr, &hostfxr_ctx);
    if (rc != 0 || hostfxr_ctx == nullptr)
    {
        write_error(pipe, rc, L"hostfxr_initialize_for_dotnet_command_line() failed");
        goto cleanup;
    }

    // Get the load assembly function pointer
    load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = nullptr;
    rc = get_delegate_fptr(
        hostfxr_ctx,
        hdt_load_assembly_and_get_function_pointer,
        (void **)&load_assembly_and_get_function_pointer);

    close_fptr(hostfxr_ctx);
    hostfxr_ctx = nullptr;

    if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
    {
        write_error(pipe, rc, L"hostfxr_get_runtime_delegate() failed");
        goto cleanup;
    }

    //
    // STEP 3: Load managed assembly and get function pointer to a managed method
    //
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
        write_error(pipe, rc, L"hostfxr_load_assembly_and_get_function_pointer() failed");
        goto cleanup;
    }

    try
    {
        dotnet_main(args);
    }
    catch (...)
    {
        // FUTURE: Figure out how to get the exception message to return back
        rc = 1;
        write_error(pipe, rc, L"Unknown dotnet hosting error");
        goto cleanup;
    }

    rc = 0;

cleanup:
    if (close_fptr != nullptr && hostfxr_ctx != nullptr)
    {
        close_fptr(hostfxr_ctx);
    }
    if (hostfxr != nullptr)
    {
        ::FreeLibrary(hostfxr);
    }

    return rc;
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD dwReason, LPVOID lpReserved)
{
    return TRUE;
}
