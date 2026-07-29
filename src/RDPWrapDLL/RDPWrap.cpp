/*
  Copyright 2021 anhkgg.

  1. fix some bugs.
  2. make code clean.
  3. add Hook and PatchFunction.

  Copyright 2014 Stas'M Corp.

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.
*/

#include "stdafx.h"
#include "IniFile.h"
#include <stdlib.h>

FARJMP Old_SLGetWindowsInformationDWORD, Stub_SLGetWindowsInformationDWORD;
SLGETWINDOWSINFORMATIONDWORD _SLGetWindowsInformationDWORD;

extern wchar_t LogFile[256];

INI_FILE *IniFile;

HMODULE hSLC;
PLATFORM_DWORD TermSrvBase;
PLATFORM_DWORD TermSrvSize;
FILE_VERSION _FileVersion;
SERVICEMAIN _ServiceMain;
SVCHOSTPUSHSERVICEGLOBALS _SvchostPushServiceGlobals;
bool AlreadyHooked = false;


bool OverrideSL(LPWSTR ValueName, DWORD *Value)
{
	INI_VAR_DWORD Variable = {0};

	if (IniFile->VariableExists(L"SLPolicy", ValueName))
	{
		if (!(IniFile->GetVariableInSection(L"SLPolicy", ValueName, &Variable))) *Value = 0;
		else *Value = Variable.ValueDec;
		return true;
	}
	return false;
}

HRESULT WINAPI New_SLGetWindowsInformationDWORD(PWSTR pwszValueName, DWORD *pdwValue)
{
	// wrapped SLGetWindowsInformationDWORD function
	// termsrv.dll will call this function instead of original SLC.dll

	// Override SL Policy

	/*extern FARJMP Old_SLGetWindowsInformationDWORD, Stub_SLGetWindowsInformationDWORD;
	extern SLGETWINDOWSINFORMATIONDWORD _SLGetWindowsInformationDWORD;*/

	DWORD dw;
	SIZE_T bw;
	HRESULT Result;

    WriteToLog("Policy query: %S\r\n", pwszValueName);

	if (OverrideSL(pwszValueName, &dw))
	{
		*pdwValue = dw;
        WriteToLog("Policy rewrite: %i\r\n", dw);

		return S_OK;
	}

	WriteProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Old_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);
	Result = _SLGetWindowsInformationDWORD(pwszValueName, pdwValue);
	if (Result == S_OK)
	{
        WriteToLog("Policy result: %i\r\n", dw);
	} else {
		WriteToLog("Policy request failed\r\n");
	}
	WriteProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Stub_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);

	return Result;
}

HRESULT __fastcall New_Win8SL(PWSTR pwszValueName, DWORD *pdwValue)
{
	// wrapped unexported function SLGetWindowsInformationDWORDWrapper in termsrv.dll
	// for Windows 8 support

	// Override SL Policy

	//extern SLGETWINDOWSINFORMATIONDWORD _SLGetWindowsInformationDWORD;


	DWORD dw;
	HRESULT Result;

	WriteToLog("Policy query: %S\r\n", pwszValueName);

	if (OverrideSL(pwszValueName, &dw))
	{
		*pdwValue = dw;

		WriteToLog("Policy rewrite: %i\r\n", dw);

		return S_OK;
	}

	Result = _SLGetWindowsInformationDWORD(pwszValueName, pdwValue);
	if (Result == S_OK)
	{
		WriteToLog("Policy result: %i\r\n", dw);
	} else {
		WriteToLog("Policy request failed\r\n");
	}

	return Result;
}

#ifndef _WIN64
HRESULT __fastcall New_Win8SL_CP(DWORD arg1, DWORD *pdwValue, PWSTR pwszValueName, DWORD arg4)
{
	// wrapped unexported function SLGetWindowsInformationDWORDWrapper in termsrv.dll
	// for Windows 8 Consumer Preview support

	return New_Win8SL(pwszValueName, pdwValue);
}
#endif

HRESULT WINAPI New_CSLQuery_Initialize()
{
	/*extern PLATFORM_DWORD TermSrvBase;
	extern FILE_VERSION _FileVersion;*/
    //__debugbreak();

	DWORD *bServerSku = NULL;
	DWORD *bRemoteConnAllowed = NULL;
	DWORD *bFUSEnabled = NULL;
	DWORD *bAppServerAllowed = NULL;
	DWORD *bMultimonAllowed = NULL;
	DWORD *lMaxUserSessions = NULL;
	DWORD *ulMaxDebugSessions = NULL;
	DWORD *bInitialized = NULL;

	WriteToLog(">>> CSLQuery::Initialize\r\n");

	char *Sect;
	Sect = new char[256];
	memset(Sect, 0x00, 256);
	wsprintfA(Sect, "%d.%d.%d.%d-SLInit", _FileVersion.wVersion.Major, _FileVersion.wVersion.Minor, _FileVersion.Release, _FileVersion.Build);

	if (IniFile->SectionExists(Sect))
	{
		#ifdef _WIN64
		bServerSku = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bServerSku.x64", 0));
		bRemoteConnAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bRemoteConnAllowed.x64", 0));
		bFUSEnabled = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bFUSEnabled.x64", 0));
		bAppServerAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bAppServerAllowed.x64", 0));
		bMultimonAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bMultimonAllowed.x64", 0));
		lMaxUserSessions = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "lMaxUserSessions.x64", 0));
		ulMaxDebugSessions = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "ulMaxDebugSessions.x64", 0));
		bInitialized = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bInitialized.x64", 0));
		#else
		bServerSku = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bServerSku.x86", 0));
		bRemoteConnAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bRemoteConnAllowed.x86", 0));
		bFUSEnabled = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bFUSEnabled.x86", 0));
		bAppServerAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bAppServerAllowed.x86", 0));
		bMultimonAllowed = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bMultimonAllowed.x86", 0));
		lMaxUserSessions = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "lMaxUserSessions.x86", 0));
		ulMaxDebugSessions = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "ulMaxDebugSessions.x86", 0));
		bInitialized = (DWORD*)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "bInitialized.x86", 0));
		#endif
	}
	delete[] Sect;

	if (bServerSku)
	{
		*bServerSku = INIReadDWordHex(IniFile, "SLInit", "bServerSku", 1);

		WriteToLog("SLInit [0x%p] bServerSku = %d\r\n", bServerSku, *bServerSku);

	}
	if (bRemoteConnAllowed)
	{
		*bRemoteConnAllowed = INIReadDWordHex(IniFile, "SLInit", "bRemoteConnAllowed", 1);

		WriteToLog("SLInit [0x%p] bRemoteConnAllowed = %d\r\n", bRemoteConnAllowed, *bRemoteConnAllowed);

	}
	if (bFUSEnabled)
	{
		*bFUSEnabled = INIReadDWordHex(IniFile, "SLInit", "bFUSEnabled", 1);

		WriteToLog("SLInit [0x%p] bFUSEnabled = %d\r\n", bFUSEnabled, *bFUSEnabled);

	}
	if (bAppServerAllowed)
	{
		*bAppServerAllowed = INIReadDWordHex(IniFile, "SLInit", "bAppServerAllowed", 1);

		WriteToLog("SLInit [0x%p] bAppServerAllowed = %d\r\n", bAppServerAllowed, *bAppServerAllowed);

	}
	if (bMultimonAllowed)
	{
		*bMultimonAllowed = INIReadDWordHex(IniFile, "SLInit", "bMultimonAllowed", 1);

		WriteToLog("SLInit [0x%p] bMultimonAllowed = %d\r\n", bMultimonAllowed, *bMultimonAllowed);

	}
	if (lMaxUserSessions)
	{
		*lMaxUserSessions = INIReadDWordHex(IniFile, "SLInit", "lMaxUserSessions", 0);

		WriteToLog("SLInit [0x%p] lMaxUserSessions = %d\r\n", lMaxUserSessions, *lMaxUserSessions);

	}
	if (ulMaxDebugSessions)
	{
		*ulMaxDebugSessions = INIReadDWordHex(IniFile, "SLInit", "ulMaxDebugSessions", 0);

		WriteToLog("SLInit [0x%p] ulMaxDebugSessions = %d\r\n", ulMaxDebugSessions, *ulMaxDebugSessions);
	}
	if (bInitialized)
	{
		*bInitialized = INIReadDWordHex(IniFile, "SLInit", "bInitialized", 1);
		WriteToLog("SLInit [0x%p] bInitialized = %d\r\n", bInitialized, *bInitialized);
	}
	WriteToLog("<<< CSLQuery::Initialize\r\n");
	return S_OK;
}

#include <Shlwapi.h>
#pragma comment(lib, "shlwapi")


bool LoadConfig()
{
    //=================================================================
    WriteToLog("Loading configuration...\r\n");

    wchar_t ConfigFile[256] = { 0x00 };
    GetModuleFileName(GetCurrentModule(), ConfigFile, 255);

    /*for (DWORD i = wcslen(ConfigFile); i > 0; i--)
    {
        if (ConfigFile[i] == '.')
        {
            memset(&ConfigFile[i + 1], 0x00, ((256 - (i + 1))) * 2);
            memcpy(&ConfigFile[i + 1], L"ini", strlen("ini") * 2);
            break;
        }
    }*/
    PathRemoveExtensionW(ConfigFile);
    wcscat_s(ConfigFile, L".ini");
    // PathRemoveFileSpecW(ConfigFile);
    // PathAppendW(ConfigFile, "rdpwrap.ini");

    WriteToLog("Configuration file: %S\r\n", ConfigFile);

    IniFile = new INI_FILE(ConfigFile);

    // TODO: implement this
    if (IniFile == NULL)
    {
        WriteToLog("Error: Failed to load configuration\r\n");
        return false;
    }

    //=================================================================
    //get logfile path

    INI_VAR_STRING LogFileVar;

    if (!(IniFile->GetVariableInSection("Main", "LogFile", &LogFileVar)))
    {
        GetModuleFileName(GetCurrentModule(), LogFile, 255);
        for (DWORD i = wcslen(LogFile); i > 0; i--)
        {
            if (LogFile[i] == '\\')
            {
                memset(&LogFile[i + 1], 0x00, ((256 - (i + 1))) * 2);
                memcpy(&LogFile[i + 1], L"rdpwrap.txt", strlen("rdpwrap.txt") * 2);
                break;
            }
        }
    }
    else
    {
        // TODO: Change it before add UNICODE in IniFile
        wchar_t wcLogFile[256];
        memset(wcLogFile, 0x00, 256);
        mbstowcs(wcLogFile, LogFileVar.Value, 255);
        wcscpy(LogFile, wcLogFile);
    }

    return true;
}

bool LoadSystemTermSrv()
{
    WCHAR path[MAX_PATH] = { 0 };
    GetSystemDirectory(path, MAX_PATH);
    PathAppend(path, L"termsrv.dll");
    HMODULE hTermSrv = LoadLibrary(path);
    if (hTermSrv == 0)
    {
        WriteToLog("Error: Failed to load Terminal Services library\r\n");
        return false;
    }
    _ServiceMain = (SERVICEMAIN)GetProcAddress(hTermSrv, "ServiceMain");
    _SvchostPushServiceGlobals = (SVCHOSTPUSHSERVICEGLOBALS)GetProcAddress(hTermSrv, "SvchostPushServiceGlobals");

    WriteToLog(
        "Base addr:  0x%p\r\n"
        "SvcMain:    termsrv.dll+0x%p\r\n"
        "SvcGlobals: termsrv.dll+0x%p\r\n",
        hTermSrv,
        (PLATFORM_DWORD)_ServiceMain - (PLATFORM_DWORD)hTermSrv,
        (PLATFORM_DWORD)_SvchostPushServiceGlobals - (PLATFORM_DWORD)hTermSrv);

    if (!GetModuleCodeSectionInfo(hTermSrv, &TermSrvBase, &TermSrvSize)) {
        WriteToLog("Error: Failed to get Terminal Services library information\r\n");
        return false;
    }

    return true;
}

bool CheckTermSrvVersion(WORD *Ver)
{
    // check termsrv version
    if (GetModuleVersion(L"termsrv.dll", &_FileVersion))
    {
        *Ver = (BYTE)_FileVersion.wVersion.Minor | ((BYTE)_FileVersion.wVersion.Major << 8);
    }
    else {
        // check NT version
        // Ver = GetVersion(); // deprecated
        // Ver = ((Ver & 0xFF) << 8) | ((Ver & 0xFF00) >> 8);
    }
    if (*Ver == 0)
    {
        WriteToLog("Error: Failed to detect Terminal Services version\r\n");
        return false;
    }

    WriteToLog("Version:    %d.%d.%d.%d\r\n", _FileVersion.wVersion.Major, _FileVersion.wVersion.Minor, _FileVersion.Release, _FileVersion.Build);

    return true;
}

bool PatchSLPolicy(WORD VersionNumber)
{
    bool Bool; //默认为真
    SIZE_T bw;

    if (!(IniFile->GetVariableInSection("Main", "SLPolicyHookNT60", &Bool))) Bool = true;

    if ((VersionNumber == 0x0600) && Bool)
    {
        // Windows Vista
        // uses SL Policy API (slc.dll)

        // load slc.dll and hook function
        hSLC = LoadLibrary(L"slc.dll");
        _SLGetWindowsInformationDWORD = (SLGETWINDOWSINFORMATIONDWORD)GetProcAddress(hSLC, "SLGetWindowsInformationDWORD");
        if (_SLGetWindowsInformationDWORD != INVALID_HANDLE_VALUE)
        {
            // rewrite original function to call our function (make hook)

            WriteToLog("Hook SLGetWindowsInformationDWORD\r\n");
#ifdef _WIN64
            Stub_SLGetWindowsInformationDWORD.MovOp = 0x48;
            Stub_SLGetWindowsInformationDWORD.MovRegArg = 0xB8;
            Stub_SLGetWindowsInformationDWORD.MovArg = (PLATFORM_DWORD)New_SLGetWindowsInformationDWORD;
            Stub_SLGetWindowsInformationDWORD.PushRaxOp = 0x50;
            Stub_SLGetWindowsInformationDWORD.RetOp = 0xC3;
#else
            Stub_SLGetWindowsInformationDWORD.PushOp = 0x68;
            Stub_SLGetWindowsInformationDWORD.PushArg = (PLATFORM_DWORD)New_SLGetWindowsInformationDWORD;
            Stub_SLGetWindowsInformationDWORD.RetOp = 0xC3;
#endif

            ReadProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Old_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);
            WriteProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Stub_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);
        }
    }

    if (!(IniFile->GetVariableInSection("Main", "SLPolicyHookNT61", &Bool))) Bool = true;

    if ((VersionNumber == 0x0601) && Bool)
    {
        // Windows 7
        // uses SL Policy API (slc.dll)

        // load slc.dll and hook function
        hSLC = LoadLibrary(L"slc.dll");
        _SLGetWindowsInformationDWORD = (SLGETWINDOWSINFORMATIONDWORD)GetProcAddress(hSLC, "SLGetWindowsInformationDWORD");
        if (_SLGetWindowsInformationDWORD != INVALID_HANDLE_VALUE)
        {
            // rewrite original function to call our function (make hook)

            WriteToLog("Hook SLGetWindowsInformationDWORD\r\n");
#ifdef _WIN64
            Stub_SLGetWindowsInformationDWORD.MovOp = 0x48;
            Stub_SLGetWindowsInformationDWORD.MovRegArg = 0xB8;
            Stub_SLGetWindowsInformationDWORD.MovArg = (PLATFORM_DWORD)New_SLGetWindowsInformationDWORD;
            Stub_SLGetWindowsInformationDWORD.PushRaxOp = 0x50;
            Stub_SLGetWindowsInformationDWORD.RetOp = 0xC3;
#else
            Stub_SLGetWindowsInformationDWORD.PushOp = 0x68;
            Stub_SLGetWindowsInformationDWORD.PushArg = (PLATFORM_DWORD)New_SLGetWindowsInformationDWORD;
            Stub_SLGetWindowsInformationDWORD.RetOp = 0xC3;
#endif

            ReadProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Old_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);
            WriteProcessMemory(GetCurrentProcess(), _SLGetWindowsInformationDWORD, &Stub_SLGetWindowsInformationDWORD, sizeof(FARJMP), &bw);
        }
    }
    if (VersionNumber == 0x0602)
    {
        // Windows 8
        // uses SL Policy internal unexported function

        // load slc.dll and get function
        // (will be used on intercepting undefined values)
        hSLC = LoadLibrary(L"slc.dll");
        _SLGetWindowsInformationDWORD = (SLGETWINDOWSINFORMATIONDWORD)GetProcAddress(hSLC, "SLGetWindowsInformationDWORD");
    }
    if (VersionNumber == 0x0603)
    {
        // Windows 8.1
        // uses SL Policy internal inline code
    }
    if (VersionNumber == 0x0604)
    {
        // Windows 10
        // uses SL Policy internal inline code
    }

    return true;
}

bool PatchFunction(char* section, char* IfPatchFieldName, char* PathOffsetFieldName, char* PathCodeFieldName, char* log)
{
    bool Bool;
    PLATFORM_DWORD SignPtr;
    SIZE_T bw;
    INI_VAR_STRING PatchName;
    INI_VAR_BYTEARRAY Patch;

    if (!(IniFile->GetVariableInSection(section, IfPatchFieldName, &Bool))) Bool = false;

    if (Bool)
    {
        WriteToLog(log);
        Bool = false;
        //address to patch
        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, section, PathOffsetFieldName, 0));
        Bool = IniFile->GetVariableInSection(section, PathCodeFieldName, &PatchName);
        if (Bool) {
            Bool = IniFile->GetVariableInSection("PatchCodes", PatchName.Value, &Patch);
            WriteToLog("PatchFunc: %s SignPtr=0x%p offset=0x%X patchCode=%s patchSize=%d\r\n",
                IfPatchFieldName, (LPVOID)SignPtr, (unsigned int)(SignPtr - TermSrvBase), PatchName.Value, Patch.ArraySize);
            if (Bool && (SignPtr > TermSrvBase)) {
                // Log original bytes
                BYTE orig[16] = {0};
                memcpy(orig, (LPVOID)SignPtr, Patch.ArraySize > 16 ? 16 : Patch.ArraySize);
                char origHex[128] = {0};
                for(DWORD k = 0; k < (Patch.ArraySize > 16 ? 16 : Patch.ArraySize); k++)
                    wsprintfA(origHex + strlen(origHex), "%02X ", orig[k]);
                WriteToLog("PatchFunc: Original: %s\r\n", origHex);
                // Write patch with VirtualProtect
                DWORD oldProt = 0;
                VirtualProtect((LPVOID)SignPtr, Patch.ArraySize, PAGE_EXECUTE_READWRITE, &oldProt);
                BOOL wpmResult = WriteProcessMemory(GetCurrentProcess(), (LPVOID)SignPtr, Patch.Value, Patch.ArraySize, &bw);
                // Log patch bytes
                char patchHex[128] = {0};
                for(DWORD k = 0; k < (Patch.ArraySize > 16 ? 16 : Patch.ArraySize); k++)
                    wsprintfA(patchHex + strlen(patchHex), "%02X ", Patch.Value[k]);
                WriteToLog("PatchFunc: Patch: %s WPM=%d bw=%d\r\n", patchHex, wpmResult, (int)bw);
                // Readback
                BYTE readback[16] = {0};
                memcpy(readback, (LPVOID)SignPtr, Patch.ArraySize > 16 ? 16 : Patch.ArraySize);
                char readHex[128] = {0};
                for(DWORD k = 0; k < (Patch.ArraySize > 16 ? 16 : Patch.ArraySize); k++)
                    wsprintfA(readHex + strlen(readHex), "%02X ", readback[k]);
                WriteToLog("PatchFunc: Readback: %s\r\n", readHex);
                // Restore protection
                DWORD tmpProt = 0;
                VirtualProtect((LPVOID)SignPtr, Patch.ArraySize, oldProt, &tmpProt);
                FlushInstructionCache(GetCurrentProcess(), (LPVOID)SignPtr, Patch.ArraySize);
            } else {
                WriteToLog("PatchFunc: %s SKIP (SignPtr<=Base or patchCode not found)\r\n", IfPatchFieldName);
            }
        } else {
            WriteToLog("PatchFunc: %s patchCode name not found in INI\r\n", IfPatchFieldName);
        }
    }

    return true;
}

//bool PatchJumpX64(char* section, char* IfPatchFieldName, char* PathOffsetFieldName, char* FakeFuctionName, char* DefaultFunctionName, char* function, char* log)
//{
//    bool Bool;
//    PLATFORM_DWORD SignPtr;
//    FARJMP Jump;
//    SIZE_T bw;
//
//    if (!(IniFile->GetVariableInSection(section, IfPatchFieldName, &Bool))) Bool = false;
//
//    if (Bool)
//    {
//        WriteToLog(log);
//
//        char *FuncName;
//        FuncName = new char[1024];
//
//        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, section, PathOffsetFieldName, 0));
//        Jump.MovOp = 0x48;
//        Jump.MovRegArg = 0xB8;
//        Jump.MovArg = (PLATFORM_DWORD)function;
//        Jump.PushRaxOp = 0x50;
//        Jump.RetOp = 0xC3;
//
//        INIReadString(IniFile, section, FakeFuctionName, DefaultFunctionName, FuncName, 1024);
//
//        if (strcmp(FuncName, DefaultFunctionName))
//        {
//            Jump.MovArg = (PLATFORM_DWORD)function;
//        }
//
//        delete[] FuncName;
//        if (SignPtr > TermSrvBase) WriteProcessMemory(GetCurrentProcess(), (LPVOID)SignPtr, &Jump, sizeof(FARJMP), &bw);
//    }
//
//    return true;
//}

void PatchSLPolicyInternal(char* Sect)
{
    bool Bool;
    PLATFORM_DWORD SignPtr;
    FARJMP Jump;
    SIZE_T bw;

#ifdef _WIN64
    if (!(IniFile->GetVariableInSection(Sect, "SLPolicyInternal.x64", &Bool))) Bool = false;
#else
    if (!(IniFile->GetVariableInSection(Sect, "SLPolicyInternal.x86", &Bool))) Bool = false;
#endif
    if (Bool)
    {
        WriteToLog("Hook SLGetWindowsInformationDWORDWrapper\r\n");
        char *FuncName;
        FuncName = new char[1024];
#ifdef _WIN64
        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "SLPolicyOffset.x64", 0));
        Jump.MovOp = 0x48;
        Jump.MovRegArg = 0xB8;
        Jump.MovArg = (PLATFORM_DWORD)New_Win8SL;
        Jump.PushRaxOp = 0x50;
        Jump.RetOp = 0xC3;

        INIReadString(IniFile, Sect, "SLPolicyFunc.x64", "New_Win8SL", FuncName, 1024);

        if (strcmp(FuncName, "New_Win8SL"))
        {
            Jump.MovArg = (PLATFORM_DWORD)New_Win8SL;
        }
#else
        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "SLPolicyOffset.x86", 0));
        Jump.PushOp = 0x68;
        Jump.PushArg = (PLATFORM_DWORD)New_Win8SL;
        Jump.RetOp = 0xC3;

        INIReadString(IniFile, Sect, "SLPolicyFunc.x86", "New_Win8SL", FuncName, 1024);

        if (strcmp(FuncName, "New_Win8SL"))
        {
            Jump.PushArg = (PLATFORM_DWORD)New_Win8SL;
        }
        if (strcmp(FuncName, "New_Win8SL_CP"))
        {
            Jump.PushArg = (PLATFORM_DWORD)New_Win8SL_CP;
        }
#endif
        delete[] FuncName;
        if (SignPtr > TermSrvBase) WriteProcessMemory(GetCurrentProcess(), (LPVOID)SignPtr, &Jump, sizeof(FARJMP), &bw);
    }
}

void PatchSLInit(char* Sect)
{
    bool Bool;
    PLATFORM_DWORD SignPtr;
    FARJMP Jump;
    SIZE_T bw;

#ifdef _WIN64
    if (!(IniFile->GetVariableInSection(Sect, "SLInitHook.x64", &Bool))) Bool = false;
#else
    if (!(IniFile->GetVariableInSection(Sect, "SLInitHook.x86", &Bool))) Bool = false;
#endif
    if (Bool)
    {
        WriteToLog("Hook CSLQuery::Initialize\r\n");
        char *FuncName;
        FuncName = new char[1024];
#ifdef _WIN64
        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "SLInitOffset.x64", 0));
        // FARJMP struct must be packed(1) to avoid alignment padding
        // See stdafx.h for #pragma pack(push, 1)
        Jump.MovOp = 0x48;
        Jump.MovRegArg = 0xB8;
        Jump.MovArg = (PLATFORM_DWORD)New_CSLQuery_Initialize;
        Jump.PushRaxOp = 0x50;
        Jump.RetOp = 0xC3;

        INIReadString(IniFile, Sect, "SLInitFunc.x64", "New_CSLQuery_Initialize", FuncName, 1024);

        if (strcmp(FuncName, "New_CSLQuery_Initialize"))
        {
            Jump.MovArg = (PLATFORM_DWORD)New_CSLQuery_Initialize;
        }
#else
        SignPtr = (PLATFORM_DWORD)(TermSrvBase + INIReadDWordHex(IniFile, Sect, "SLInitOffset.x86", 0));
        Jump.PushOp = 0x68;
        Jump.PushArg = (PLATFORM_DWORD)New_CSLQuery_Initialize;
        Jump.RetOp = 0xC3;

        INIReadString(IniFile, Sect, "SLInitFunc.x86", "New_CSLQuery_Initialize", FuncName, 1024);

        if (strcmp(FuncName, "New_CSLQuery_Initialize"))
        {
            Jump.PushArg = (PLATFORM_DWORD)New_CSLQuery_Initialize;
        }
#endif
        delete[] FuncName;
        WriteToLog("SLInitHook: TermSrvBase=0x%p SLInitOffset=0x%X sizeof(FARJMP)=%d\r\n",
            (LPVOID)TermSrvBase, (unsigned int)(SignPtr - TermSrvBase), (int)sizeof(FARJMP));
        WriteToLog("SLInitHook: SignPtr=0x%p New_CSLQuery_Initialize=0x%p\r\n",
            (LPVOID)SignPtr, (LPVOID)New_CSLQuery_Initialize);
        if (SignPtr > TermSrvBase) {
            BYTE origBytes[12] = {0};
            memcpy(origBytes, (LPVOID)SignPtr, 12);
            WriteToLog("SLInitHook: Original bytes: %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X\r\n",
                origBytes[0], origBytes[1], origBytes[2], origBytes[3],
                origBytes[4], origBytes[5], origBytes[6], origBytes[7],
                origBytes[8], origBytes[9], origBytes[10], origBytes[11]);
            DWORD oldProtect = 0;
            BOOL vpResult = VirtualProtect((LPVOID)SignPtr, sizeof(FARJMP), PAGE_EXECUTE_READWRITE, &oldProtect);
            WriteToLog("SLInitHook: SignPtr=0x%p VirtualProtect=%d oldProtect=0x%X\r\n", (LPVOID)SignPtr, vpResult, oldProtect);
            WriteToLog("SLInitHook: Jump bytes: %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X\r\n",
                ((BYTE*)&Jump)[0], ((BYTE*)&Jump)[1], ((BYTE*)&Jump)[2], ((BYTE*)&Jump)[3],
                ((BYTE*)&Jump)[4], ((BYTE*)&Jump)[5], ((BYTE*)&Jump)[6], ((BYTE*)&Jump)[7],
                ((BYTE*)&Jump)[8], ((BYTE*)&Jump)[9], ((BYTE*)&Jump)[10], ((BYTE*)&Jump)[11]);
            BOOL wpmResult = WriteProcessMemory(GetCurrentProcess(), (LPVOID)SignPtr, &Jump, sizeof(FARJMP), &bw);
            WriteToLog("SLInitHook: WriteProcessMemory=%d bytesWritten=%d\r\n", wpmResult, bw);
            BYTE readback[12] = {0};
            memcpy(readback, (LPVOID)SignPtr, 12);
            WriteToLog("SLInitHook: Readback: %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X %02X\r\n",
                readback[0], readback[1], readback[2], readback[3],
                readback[4], readback[5], readback[6], readback[7],
                readback[8], readback[9], readback[10], readback[11]);
            if (vpResult) {
                DWORD tempProtect = 0;
                VirtualProtect((LPVOID)SignPtr, sizeof(FARJMP), oldProtect, &tempProtect);
            }
            FlushInstructionCache(GetCurrentProcess(), (LPVOID)SignPtr, sizeof(FARJMP));
            WriteToLog("SLInitHook: FlushInstructionCache done\r\n");
            WriteToLog("SLInitHook: Writing SLInit data directly\r\n");
            {
                char *SectSLInit = new char[256];
                memset(SectSLInit, 0x00, 256);
                wsprintfA(SectSLInit, "%d.%d.%d.%d-SLInit", _FileVersion.wVersion.Major, _FileVersion.wVersion.Minor, _FileVersion.Release, _FileVersion.Build);
                WriteToLog("SLInitDirect: Section=[%s]\r\n", SectSLInit);
                if (IniFile->SectionExists(SectSLInit)) {
                    struct SLEntry { const char* key; const char* name; DWORD value; };
                    SLEntry entries[] = {
                        {"bInitialized.x64", "bInitialized", 1},
                        {"bServerSku.x64", "bServerSku", 1},
                        {"lMaxUserSessions.x64", "lMaxUserSessions", 0},
                        {"bAppServerAllowed.x64", "bAppServerAllowed", 1},
                        {"bRemoteConnAllowed.x64", "bRemoteConnAllowed", 1},
                        {"bMultimonAllowed.x64", "bMultimonAllowed", 1},
                        {"ulMaxDebugSessions.x64", "ulMaxDebugSessions", 0},
                        {"bFUSEnabled.x64", "bFUSEnabled", 1}
                    };
                    for (int i = 0; i < 8; i++) {
                        DWORD offset = INIReadDWordHex(IniFile, SectSLInit, (char*)entries[i].key, 0);
                        WriteToLog("SLInitDirect: %s offset=0x%X\r\n", entries[i].name, offset);
                        if (offset > 0) {
                            PLATFORM_DWORD addr = TermSrvBase + offset;
                            __try {
                                DWORD oldProt = 0;
                                VirtualProtect((LPVOID)addr, sizeof(DWORD), PAGE_READWRITE, &oldProt);
                                *(DWORD*)addr = entries[i].value;
                                DWORD readback = *(DWORD*)addr;
                                WriteToLog("SLInitDirect: %s [0x%p] = %d (readback=%d)\r\n",
                                    entries[i].name, (LPVOID)addr, entries[i].value, readback);
                                if (oldProt != PAGE_READWRITE) {
                                    DWORD tmpProt = 0;
                                    VirtualProtect((LPVOID)addr, sizeof(DWORD), oldProt, &tmpProt);
                                }
                            } __except(EXCEPTION_EXECUTE_HANDLER) {
                                WriteToLog("SLInitDirect: EXCEPTION writing %s at 0x%p (code=0x%X)\r\n",
                                    entries[i].name, (LPVOID)addr, GetExceptionCode());
                            }
                        } else {
                            WriteToLog("SLInitDirect: %s offset=0, skipped\r\n", entries[i].name);
                        }
                    }
                } else {
                    WriteToLog("SLInitDirect: Section not found!\r\n");
                }
                delete[] SectSLInit;
            }
            WriteToLog("SLInitHook: Direct write done\r\n");
        } else {
            WriteToLog("SLInitHook: ERROR SignPtr(0x%p) <= TermSrvBase(0x%p)\r\n", (LPVOID)SignPtr, (LPVOID)TermSrvBase);
        }
    }
}

void Hook()
{
    WORD VersionNumber = 0;

    AlreadyHooked = true;

    if (!LoadConfig()) {
        return;
    }

	WriteToLog("Initializing RDP Wrapper...\r\n");

    if (!LoadSystemTermSrv()) {
        return;
    }

    if (!CheckTermSrvVersion(&VersionNumber)) {
        return;
    }

	// temporarily freeze threads
	WriteToLog("Freezing threads...\r\n");
	SetThreadsState(false);

    PatchSLPolicy(VersionNumber);

    char Sect[256] = { 0 };
	wsprintfA(Sect, "%d.%d.%d.%d", _FileVersion.wVersion.Major, _FileVersion.wVersion.Minor, _FileVersion.Release, _FileVersion.Build);

    
	if (IniFile->SectionExists(Sect))
	{
		//if (GetModuleCodeSectionInfo(hTermSrv, &TermSrvBase, &TermSrvSize))
        if(TermSrvBase && TermSrvSize)
		{
#ifdef _WIN64
            PatchFunction(Sect, "LocalOnlyPatch.x64", "LocalOnlyOffset.x64", "LocalOnlyCode.x64", "Patch CEnforcementCore::GetInstanceOfTSLicense\r\n");
#else
            PatchFunction(Sect, "LocalOnlyPatch.x86", "LocalOnlyOffset.x86", "LocalOnlyCode.x86", "Patch CEnforcementCore::GetInstanceOfTSLicense\r\n");
#endif

#ifdef _WIN64
            PatchFunction(Sect, "SingleUserPatch.x64", "SingleUserOffset.x64", "SingleUserCode.x64", "Patch CSessionArbitrationHelper::IsSingleSessionPerUserEnabled\r\n");
#else
            PatchFunction(Sect, "SingleUserPatch.x86", "SingleUserOffset.x86", "SingleUserCode.x86", "Patch CSessionArbitrationHelper::IsSingleSessionPerUserEnabled\r\n");
#endif

#ifdef _WIN64
            PatchFunction(Sect, "DefPolicyPatch.x64", "DefPolicyOffset.x64", "DefPolicyCode.x64", "Patch CDefPolicy::Query\r\n");
#else
            PatchFunction(Sect, "DefPolicyPatch.x86", "DefPolicyOffset.x86", "DefPolicyCode.x86", "Patch CDefPolicy::Query\r\n");
#endif

//#ifdef _WIN64
//            PatchJumpX64(Sect, "SLPolicyInternal.x64", "SLPolicyOffset.x64","SLPolicyFunc.x64","New_Win8SL", (char*)New_Win8SL, "Hook SLGetWindowsInformationDWORDWrapper\r\n");
//#else
//            PatchJumpX86(Sect, "SLPolicyInternal.x86", "SLPolicyOffset.x86", "SLPolicyFunc.x86", "New_Win8SL", (char*)New_Win8SL, "Hook SLGetWindowsInformationDWORDWrapper\r\n");
//#endif
			
            PatchSLPolicyInternal(Sect);

            PatchSLInit(Sect);
		}
	}

	WriteToLog("Resumimg threads...\r\n");
	SetThreadsState(true);
	return;
}

void WINAPI ServiceMain(DWORD dwArgc, LPTSTR *lpszArgv)
{
	WriteToLog(">>> ServiceMain\r\n");
	if (!AlreadyHooked) Hook();

	if (_ServiceMain != NULL) _ServiceMain(dwArgc, lpszArgv);
	WriteToLog("<<< ServiceMain\r\n");
}

void WINAPI SvchostPushServiceGlobals(void *lpGlobalData)
{
	WriteToLog(">>> SvchostPushServiceGlobals\r\n");
	if (!AlreadyHooked) Hook();

	if (_SvchostPushServiceGlobals != NULL) _SvchostPushServiceGlobals(lpGlobalData);
	WriteToLog("<<< SvchostPushServiceGlobals\r\n");
}
