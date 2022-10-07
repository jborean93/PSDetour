#include <iostream>
#include <filesystem>
#include <fstream>
#include <assert.h>

#include <nethost.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>

#include <Windows.h>

// https://github.com/dotnet/samples/tree/main/core/hosting/src/NativeHost

typedef int(NETHOST_CALLTYPE *get_hostfxr_path_fn)(char_t *buffer, size_t *buffer_size, const struct get_hostfxr_parameters *parameters);

namespace
{
    HINSTANCE dll_module = nullptr;

    // Globals to hold hostfxr exports
    get_hostfxr_path_fn hostfxr_path_fptr;
    hostfxr_initialize_for_runtime_config_fn init_fptr;
    hostfxr_get_runtime_delegate_fn get_delegate_fptr;
    hostfxr_close_fn close_fptr;

    // Forward declarations
    bool load_hostfxr(const char_t *);
    load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *assembly);
}

extern "C" __declspec(dllexport) int inject(void *pipe)
{
    //
    // STEP 0: Get the current executable directory
    //
    std::basic_string<wchar_t> module_path(MAX_PATH, '\0');
    DWORD copied = 0;
    for (;;)
    {
        auto length = ::GetModuleFileNameW(dll_module, &module_path[0], module_path.length());
        if (length < module_path.length() - 1)
        {
            module_path.resize(length);
            module_path.shrink_to_fit();
            break;
        }

        module_path.resize(module_path.length() * 2);
    }

    //
    // STEP 1: Load HostFxr and get exported hosting functions
    //
    std::filesystem::path nethost_path{module_path};
    nethost_path.replace_filename(L"nethost.dll");
    if (!load_hostfxr(nethost_path.c_str()))
    {
        assert(false && "Failure: load_hostfxr()");
        return 1;
    }

    //
    // STEP 2: Initialize and start the .NET Core runtime
    //
    std::filesystem::path config_path{module_path};
    config_path.replace_filename(L"PSDetour.runtimeconfig.json");
    load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = nullptr;
    load_assembly_and_get_function_pointer = get_dotnet_load_assembly(config_path.c_str());
    assert(load_assembly_and_get_function_pointer != nullptr && "Failure: get_dotnet_load_assembly()");

    //
    // STEP 3: Load managed assembly and get function pointer to a managed method
    //
    std::filesystem::path dotnetlib_path{module_path};
    dotnetlib_path.replace_filename(L"PSDetour.dll");
    component_entry_point_fn dotnet_main = nullptr;
    int rc = load_assembly_and_get_function_pointer(
        dotnetlib_path.c_str(),
        L"PSDetour.RemoteWorker, PSDetour",
        L"Main",
        nullptr,
        nullptr,
        (void **)&dotnet_main);
    assert(rc == 0 && dotnet_main != nullptr && "Failure: load_assembly_and_get_function_pointer()");

    //
    // STEP 4: Run managed code
    //
    return dotnet_main(pipe, sizeof(void *));
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
    bool load_hostfxr(const char_t *nethost_path)
    {
        // Pre-allocate a large buffer for the path to hostfxr
        char_t buffer[MAX_PATH];
        size_t buffer_size = sizeof(buffer) / sizeof(char_t);

        void *nethost_lib = load_library(nethost_path);
        hostfxr_path_fptr = (get_hostfxr_path_fn)get_export(nethost_lib, "get_hostfxr_path");
        int rc = hostfxr_path_fptr(buffer, &buffer_size, nullptr);
        if (rc != 0)
            return false;
        // FIXME: Unload nethost_lib

        // Load hostfxr and get desired exports
        void *lib = load_library(buffer);
        init_fptr = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
        get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
        close_fptr = (hostfxr_close_fn)get_export(lib, "hostfxr_close");

        return (init_fptr && get_delegate_fptr && close_fptr);
    }

    // Load and initialize .NET Core and get desired function pointer for scenario
    load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const char_t *config_path)
    {
        // Load .NET Core
        void *load_assembly_and_get_function_pointer = nullptr;
        hostfxr_handle cxt = nullptr;
        int rc = init_fptr(config_path, nullptr, &cxt);
        if ((rc != 0 && rc != 1) || cxt == nullptr)
        {
            close_fptr(cxt);
            return nullptr;
        }

        // Get the load assembly function pointer
        rc = get_delegate_fptr(
            cxt,
            hdt_load_assembly_and_get_function_pointer,
            &load_assembly_and_get_function_pointer);
        if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
            return nullptr;

        close_fptr(cxt);
        return (load_assembly_and_get_function_pointer_fn)load_assembly_and_get_function_pointer;
    }
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD dwReason, LPVOID lpReserved)
{
    switch (dwReason)
    {
    case DLL_PROCESS_ATTACH:
        dll_module = hinstDLL;
        break;
    case DLL_PROCESS_DETACH:
        dll_module = nullptr;
        break;
    }

    return TRUE;
}
