#include "application_loopback.h"

#include "device_loop.h"

#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <tlhelp32.h>
#include <wrl/implements.h>

#include <algorithm>
#include <cwctype>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        // Activation is quick; this only keeps a stuck activation from holding the source's thread.
        constexpr DWORD ActivationTimeoutMilliseconds = 5000;

        bool SameName(const std::wstring& a, const std::wstring& b)
        {
            return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(), [](wchar_t x, wchar_t y) { return std::towlower(x) == std::towlower(y); });
        }

        // Activation calls back on a thread of its own when it is done, so the handler must be agile.
        class Activation : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, Microsoft::WRL::FtmBase,
                               IActivateAudioInterfaceCompletionHandler>
        {
        public:
            Activation()
                : _done(CreateEventW(nullptr, TRUE, FALSE, nullptr))
            {
            }

            ~Activation() override
            {
                if (_done != nullptr)
                {
                    CloseHandle(_done);
                }
            }

            HANDLE Done() const
            {
                return _done;
            }

            STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation*) override
            {
                SetEvent(_done);
                return S_OK;
            }

        private:
            HANDLE _done;
        };

        struct LargestWindow
        {
            const std::wstring* executable;
            DWORD process = 0;
            int64_t area = 0;
        };

        BOOL CALLBACK Measure(HWND window, LPARAM parameter)
        {
            auto largest = reinterpret_cast<LargestWindow*>(parameter);
            RECT client = {};
            if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER) != nullptr || GetWindowTextLengthW(window) == 0 || !GetClientRect(window, &client))
            {
                return TRUE;
            }

            DWORD process = 0;
            GetWindowThreadProcessId(window, &process);
            int64_t area = static_cast<int64_t>(client.right) * client.bottom;
            if (area <= largest->area)
            {
                return TRUE;
            }

            HANDLE handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process);
            if (handle == nullptr)
            {
                return TRUE;
            }

            wchar_t path[MAX_PATH * 2] = {};
            DWORD length = static_cast<DWORD>(std::size(path));
            bool named = QueryFullProcessImageNameW(handle, 0, path, &length) != FALSE;
            CloseHandle(handle);
            std::wstring name(path, named ? length : 0);
            size_t slash = name.find_last_of(L"\\/");
            if (named && SameName(slash == std::wstring::npos ? name : name.substr(slash + 1), *largest->executable))
            {
                largest->process = process;
                largest->area = area;
            }

            return TRUE;
        }

        // The process that shows the program's largest window, as Capture chooses it (capture detailed design 8.6);
        // 0 when it shows none.
        DWORD WindowProcess(const std::wstring& executable)
        {
            LargestWindow largest{ &executable };
            EnumWindows(&Measure, reinterpret_cast<LPARAM>(&largest));
            return largest.process;
        }

        std::vector<ProcessEntry> Processes()
        {
            std::vector<ProcessEntry> processes;
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
            {
                return processes;
            }

            PROCESSENTRY32W entry = { sizeof(entry) };
            for (BOOL more = Process32FirstW(snapshot, &entry); more; more = Process32NextW(snapshot, &entry))
            {
                processes.push_back({ entry.th32ProcessID, entry.th32ParentProcessID, entry.szExeFile });
            }

            CloseHandle(snapshot);
            return processes;
        }

        HRESULT Activate(DWORD process, ComPtr<IAudioClient>& client)
        {
            AUDIOCLIENT_ACTIVATION_PARAMS parameters = {};
            parameters.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
            parameters.ProcessLoopbackParams.TargetProcessId = process;
            parameters.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
            PROPVARIANT activation = {};
            activation.vt = VT_BLOB;
            activation.blob.cbSize = sizeof(parameters);
            activation.blob.pBlobData = reinterpret_cast<BYTE*>(&parameters);

            ComPtr<Activation> handler = Microsoft::WRL::Make<Activation>();
            if (!handler || handler->Done() == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
            HRESULT hr = ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, __uuidof(IAudioClient), &activation, handler.Get(), &operation);
            if (FAILED(hr))
            {
                return hr;
            }

            if (WaitForSingleObject(handler->Done(), ActivationTimeoutMilliseconds) != WAIT_OBJECT_0)
            {
                return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
            }

            HRESULT activated = E_FAIL;
            ComPtr<IUnknown> result;
            hr = operation->GetActivateResult(&activated, &result);
            if (SUCCEEDED(hr))
            {
                hr = activated;
            }

            if (SUCCEEDED(hr))
            {
                hr = result ? result.As(&client) : E_POINTER;
            }

            return hr;
        }
    }

    std::wstring ApplicationOf(const std::wstring& deviceId)
    {
        const size_t prefix = std::size(ApplicationPrefix) - 1;
        return deviceId.size() > prefix && deviceId.compare(0, prefix, ApplicationPrefix) == 0 ? deviceId.substr(prefix) : std::wstring();
    }

    DWORD RootProcess(const std::vector<ProcessEntry>& processes, const std::wstring& executable)
    {
        for (const ProcessEntry& process : processes)
        {
            if (!SameName(process.executable, executable))
            {
                continue;
            }

            bool startedBySameProgram = std::any_of(processes.begin(), processes.end(), [&](const ProcessEntry& other)
            {
                return other.id == process.parent && other.id != process.id && SameName(other.executable, executable);
            });
            if (!startedBySameProgram)
            {
                return process.id;
            }
        }

        return 0;
    }

    DWORD ProcessFor(const std::vector<ProcessEntry>& processes, const std::wstring& executable, DWORD windowProcess)
    {
        auto find = [&](DWORD id) -> const ProcessEntry*
        {
            auto found = std::find_if(processes.begin(), processes.end(), [id](const ProcessEntry& process) { return process.id == id; });
            return found != processes.end() ? &*found : nullptr;
        };

        const ProcessEntry* process = windowProcess != 0 ? find(windowProcess) : nullptr;
        if (process == nullptr || !SameName(process->executable, executable))
        {
            return RootProcess(processes, executable);
        }

        // Up the program's own processes; the count bounds a loop of reused process IDs.
        for (size_t step = 0; step < processes.size(); step++)
        {
            const ProcessEntry* parent = process->parent != process->id ? find(process->parent) : nullptr;
            if (parent == nullptr || !SameName(parent->executable, executable))
            {
                break;
            }

            process = parent;
        }

        return process->id;
    }

    HRESULT OpenApplicationLoopback(const std::wstring& executable, REFERENCE_TIME bufferDuration, HANDLE events, ComPtr<IAudioClient>& client, HANDLE& ended)
    {
        ended = nullptr;
        DWORD process = ProcessFor(Processes(), executable, WindowProcess(executable));
        if (process == 0)
        {
            return E_NOTFOUND;
        }

        // Taken before the loopback, so a process that exits meanwhile is noticed rather than captured as silence.
        ended = OpenProcess(SYNCHRONIZE, FALSE, process);
        if (ended == nullptr)
        {
            return E_NOTFOUND;
        }

        HRESULT hr = Activate(process, client);

        // The loopback converts the program's mix to the format asked for, as a device's shared mode does.
        WAVEFORMATEXTENSIBLE format = FloatStereo();
        if (SUCCEEDED(hr))
        {
            hr = client->Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                bufferDuration,
                0,
                reinterpret_cast<WAVEFORMATEX*>(&format),
                nullptr);
        }

        if (SUCCEEDED(hr))
        {
            hr = client->SetEventHandle(events);
        }

        if (FAILED(hr))
        {
            CloseHandle(ended);
            ended = nullptr;
        }

        return hr;
    }
}
