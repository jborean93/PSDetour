#include <windows.h>

void VoidWithArg(int arg1)
{
}

int SetLastErrorWithValue(int errorCode)
{
    SetLastError((DWORD)errorCode);
    return errorCode;
}
