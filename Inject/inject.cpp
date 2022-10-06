#include <Windows.h>
#include <iostream>
#include <fstream>

using namespace std;

#define EXTERN_DLL_EXPORT extern "C" __declspec(dllexport)

// cl.exe /LD inject.cpp

EXTERN_DLL_EXPORT int inject(HANDLE pipe)
{
    // TODO: Look at starting CLR here and passing in pipe to it

    fstream my_file;
    my_file.open("C:\\temp\\test.txt", ios::out);
    if (my_file)
    {
        my_file << static_cast<void *>(pipe);
        // my_file << "test";
        my_file.close();
        return 0;
    }
    else
    {
        return 1;
    }
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD dwReason, LPVOID lpReserved)
{
    return TRUE;
}
