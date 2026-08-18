#include <windows.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>
#include <utility>

#pragma comment(lib, "user32.lib")

namespace
{
constexpr float kMinimumTarget = 0.05f;
constexpr float kVanillaWalkTarget = 0.35f;
constexpr float kMaximumWalkTarget = 0.60f;
constexpr float kMinimumJogTarget = 0.70f;
constexpr float kMaximumJogTarget = 1.00f;
constexpr float kLowWalkBaseStep = 0.30f / 8.0f;
constexpr float kHighWalkBaseStep = 0.25f / 7.0f;
constexpr float kJogBaseStep = 0.03f;
constexpr float kTargetEpsilon = 0.0001f;
constexpr int kProbeCodeOffset = 64;
constexpr int kAdsCodeOffset = 128;
constexpr int kRawMagnitudeOffset = 4096;
constexpr int kSelectedScaleOffset = 4100;
constexpr int kAdsRawOffset = 4104;
constexpr int kWalkAdsSelectedOffset = 4108;
constexpr int kJogAdsSelectedOffset = 4112;
constexpr int kAdsEnabledOffset = 4132;
constexpr int kAdsModeJogOffset = 4136;
constexpr int kAdsVectorOffset = 4140;
constexpr int kAdsOwnerOffset = 4168;
constexpr int kProbeOwnerOffset = 4176;
constexpr float kWalkMinimumScale = 1.0f / 7.0f;
constexpr float kWalkMaximumScale = 12.0f / 7.0f;
constexpr float kWalkAdsMinimum = 1.6875144f;
constexpr float kWalkAdsMaximum = 3.375f;
constexpr float kStandingWalkAdsAtVanillaWalk = 1.81f;
constexpr float kStandingWalkAdsAtMaximum = 2.48f;
constexpr float kStandingJogAdsAtMinimum = 3.00f;
constexpr float kStandingJogAdsAtMidpoint = 3.50f;
constexpr float kStandingAdsCap = 3.40f;
constexpr float kCrouchWalkAdsMinimum = 0.84f;
constexpr float kCrouchWalkAdsMaximum = 1.68f;
constexpr float kCrouchJogAds = 2.70f;
constexpr ULONGLONG kWheelGaitRebaseWindowMs = 250;
constexpr int kModeChangeConfirmationSamples = 1;
constexpr int kMinimumSensitivity = 0;
constexpr int kDefaultSensitivity = 50;
constexpr int kMaximumSensitivity = 100;
constexpr int kSensitivityIncrement = 5;
constexpr float kMaximumSensitivityScale = 7.0f;
constexpr ULONGLONG kSensitivityRepeatDelayMs = 400;
constexpr ULONGLONG kSensitivityRepeatIntervalMs = 100;
constexpr ULONGLONG kSettingsSaveDelayMs = 500;
constexpr ULONGLONG kSensitivityOverlayHoldMs = 2500;
constexpr ULONGLONG kSensitivityOverlayFadeMs = 350;
constexpr BYTE kSensitivityOverlayOpacity = 238;
constexpr int kSensitivityOverlayWidth = 420;
constexpr int kSensitivityOverlayHeight = 76;
constexpr int kSensitivityOverlayTopOffset = 98;
constexpr UINT_PTR kSensitivityOverlayTimerId = 2;
constexpr UINT kSensitivityOverlayFrameMs = 16;
constexpr UINT_PTR kTargetSmoothingTimerId = 3;
constexpr UINT kTargetSmoothingFrameMs = 8;
constexpr float kTargetSmoothingFullRangeMs = 160.0f;
constexpr ULONGLONG kTargetSmoothingMaxElapsedMs = 32;
constexpr wchar_t kSensitivityOverlayClassName[] = L"BetterMovementForKBMSensitivityOverlay";

struct RuntimeLayout
{
    std::uintptr_t controlSiteRva;
    std::uintptr_t probeSiteRva;
    std::uintptr_t adsSiteRva;
    std::uintptr_t adsTargetRva;
    std::uintptr_t controlTargetRva;
};

constexpr RuntimeLayout kLayout{
    0x13FDB762, 0x13FDBE99, 0x13FDBD7F, 0x00A0B240, 0x007D2800
};

constexpr std::array<std::uint8_t, 5> kOriginalProbe{ 0xF3, 0x0F, 0x11, 0x46, 0x60 };

struct RuntimeState
{
    HMODULE module{};
    const RuntimeLayout* layout{};
    std::uintptr_t imageBase{};
    std::uintptr_t controlSite{};
    std::uintptr_t probeSite{};
    std::uintptr_t adsSite{};
    std::uint8_t* cave{};
    std::array<std::uint8_t, 5> originalControl{};
    std::array<std::uint8_t, 5> controlRedirect{};
    std::array<std::uint8_t, 5> probeRedirect{};
    std::array<std::uint8_t, 5> originalAds{};
    std::array<std::uint8_t, 5> adsRedirect{};
    HHOOK mouseHook{};
    UINT_PTR timer{};
    float currentTarget{kMaximumJogTarget};
    int sensitivity{kDefaultSensitivity};
    float selectedScale{1.0f};
    float lastRawMagnitude{};
    ULONGLONG lastWheelAdjustmentTick{};
    ULONGLONG gaitDetectionSuppressedUntil{};
    bool pendingModeJog{};
    int pendingModeSamples{};
    bool lastModeJog{true};
    bool modeKnown{};
    bool stationaryTargetPending{};
    float appliedTarget{kMaximumJogTarget};
    ULONGLONG smoothingLastTick{};
    UINT_PTR smoothingTimer{};
    bool smoothingActive{};
    bool shiftBypass{};
    bool adsHeld{};
    bool adsOverrideEnabled{};
    bool crouched{};
    bool stanceKnown{};
    bool sensitivityDownHeld{};
    bool sensitivityDisplayHeld{};
    bool sensitivityUpHeld{};
    ULONGLONG sensitivityDownRepeatTick{};
    ULONGLONG sensitivityUpRepeatTick{};
    ULONGLONG sensitivityChangedTick{};
    bool settingsDirty{};
    std::wstring settingsPath{};
    int sensitivityDownKey{VK_F6};
    int sensitivityDisplayKey{VK_F7};
    int sensitivityUpKey{VK_F8};
    std::wstring sensitivityDownKeyName{L"F6"};
    std::wstring sensitivityDisplayKeyName{L"F7"};
    std::wstring sensitivityUpKeyName{L"F8"};
    HWND gameWindow{};
    HWND sensitivityOverlay{};
    ULONGLONG sensitivityOverlayFadeTick{};
    ULONGLONG sensitivityOverlayHideTick{};
    bool patchesInstalled{};
};

RuntimeState g_state{};

std::wstring ModulePath(HMODULE module)
{
    std::array<wchar_t, 32768> buffer{};
    const DWORD length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return {};
    return std::wstring(buffer.data(), length);
}

std::wstring FileName(const std::wstring& path)
{
    const auto separator = path.find_last_of(L"\\/");
    return separator == std::wstring::npos ? path : path.substr(separator + 1);
}

std::size_t ImageSize(std::uintptr_t imageBase)
{
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(imageBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0) return 0;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(imageBase + static_cast<std::uintptr_t>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) return 0;
    return nt->OptionalHeader.SizeOfImage;
}

bool Relative32(std::uintptr_t instruction, std::size_t length, std::uintptr_t target, std::int32_t& result)
{
    const auto relative = static_cast<std::int64_t>(target) - static_cast<std::int64_t>(instruction + length);
    if (relative < INT32_MIN || relative > INT32_MAX) return false;
    result = static_cast<std::int32_t>(relative);
    return true;
}

std::array<std::uint8_t, 5> OriginalControlBytes(std::uintptr_t imageBase, const RuntimeLayout& layout)
{
    std::array<std::uint8_t, 5> bytes{ 0xE8, 0, 0, 0, 0 };
    std::int32_t relative = 0;
    Relative32(imageBase + layout.controlSiteRva, bytes.size(), imageBase + layout.controlTargetRva, relative);
    std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
    return bytes;
}

std::array<std::uint8_t, 5> OriginalAdsBytes(std::uintptr_t imageBase, const RuntimeLayout& layout)
{
    std::array<std::uint8_t, 5> bytes{ 0xE8, 0, 0, 0, 0 };
    std::int32_t relative = 0;
    Relative32(imageBase + layout.adsSiteRva, bytes.size(), imageBase + layout.adsTargetRva, relative);
    std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
    return bytes;
}

bool WriteCode(void* destination, const void* source, std::size_t size)
{
    DWORD oldProtection = 0;
    if (!VirtualProtect(destination, size, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    std::memcpy(destination, source, size);
    FlushInstructionCache(GetCurrentProcess(), destination, size);
    DWORD ignored = 0;
    return VirtualProtect(destination, size, oldProtection, &ignored) != FALSE;
}

void* AllocateNear(std::uintptr_t site, std::size_t size)
{
    constexpr std::uintptr_t granularity = 0x10000;
    constexpr std::uintptr_t range = 0x70000000;
    const std::uintptr_t low = site > range ? (site - range) & ~(granularity - 1) : granularity;
    const std::uintptr_t high = (site + range) & ~(granularity - 1);
    for (std::uintptr_t distance = granularity; distance < range; distance += granularity)
    {
        const std::uintptr_t above = (site + distance) & ~(granularity - 1);
        if (above >= low && above <= high)
            if (void* allocation = VirtualAlloc(reinterpret_cast<void*>(above), size,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE)) return allocation;
        if (site >= distance)
        {
            const std::uintptr_t below = (site - distance) & ~(granularity - 1);
            if (below >= low && below <= high)
                if (void* allocation = VirtualAlloc(reinterpret_cast<void*>(below), size,
                        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE)) return allocation;
        }
    }
    return nullptr;
}

bool IsGameForeground()
{
    DWORD processId = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &processId);
    return processId == GetCurrentProcessId();
}

bool IsMoving()
{
    for (const int key : { 0x57, 0x41, 0x53, 0x44 })
        if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
    return false;
}

bool TargetIsJog(float target)
{
    return target >= kMinimumJogTarget - kTargetEpsilon;
}

std::wstring DirectoryName(const std::wstring& path)
{
    const auto separator = path.find_last_of(L"\\/");
    return separator == std::wstring::npos ? std::wstring{} : path.substr(0, separator);
}

bool IsGameForeground();
void ShowSensitivityOverlay();
void UpdateSensitivityOverlay();

std::wstring NormalizeKeyName(std::wstring name)
{
    const auto first = name.find_first_not_of(L" \t\r\n");
    if (first == std::wstring::npos) return {};
    const auto last = name.find_last_not_of(L" \t\r\n");
    name = name.substr(first, last - first + 1);
    CharUpperBuffW(name.data(), static_cast<DWORD>(name.size()));
    return name;
}

int ParseKeyName(const std::wstring& keyName, int fallback)
{
    const std::wstring name = NormalizeKeyName(keyName);
    if (name.size() == 1)
    {
        const wchar_t key = name[0];
        if ((key >= L'A' && key <= L'Z') || (key >= L'0' && key <= L'9')) return key;
    }
    if (name.size() >= 2 && name.size() <= 3 && name[0] == L'F')
    {
        int number = 0;
        for (std::size_t index = 1; index < name.size(); ++index)
        {
            if (name[index] < L'0' || name[index] > L'9') return fallback;
            number = number * 10 + (name[index] - L'0');
        }
        if (number >= 1 && number <= 24) return VK_F1 + number - 1;
    }
    constexpr std::array<std::pair<const wchar_t*, int>, 14> namedKeys{{
        {L"HOME", VK_HOME}, {L"END", VK_END}, {L"INSERT", VK_INSERT}, {L"DELETE", VK_DELETE},
        {L"PAGEUP", VK_PRIOR}, {L"PAGEDOWN", VK_NEXT}, {L"UP", VK_UP}, {L"DOWN", VK_DOWN},
        {L"LEFT", VK_LEFT}, {L"RIGHT", VK_RIGHT}, {L"TAB", VK_TAB}, {L"CAPSLOCK", VK_CAPITAL},
        {L"NUMPAD+", VK_ADD}, {L"NUMPAD-", VK_SUBTRACT}
    }};
    for (const auto& [key, code] : namedKeys)
        if (name == key) return code;
    return fallback;
}

std::wstring ReadKeySetting(const wchar_t* setting, const wchar_t* defaultName, int defaultKey, int& key)
{
    std::array<wchar_t, 64> buffer{};
    GetPrivateProfileStringW(L"Controls", setting, defaultName, buffer.data(),
        static_cast<DWORD>(buffer.size()), g_state.settingsPath.c_str());
    std::wstring name = NormalizeKeyName(buffer.data());
    key = ParseKeyName(name, defaultKey);
    if (key == defaultKey && ParseKeyName(name, 0) == 0) name = defaultName;
    return name;
}

void LoadSettings()
{
    const std::wstring moduleDirectory = DirectoryName(ModulePath(g_state.module));
    if (moduleDirectory.empty()) return;
    g_state.settingsPath = moduleDirectory + L"\\BetterMovementForKBM.ini";
    std::array<wchar_t, 8> existingSetting{};
    const bool settingsComplete =
        GetPrivateProfileStringW(L"Controls", L"Sensitivity", L"", existingSetting.data(),
            static_cast<DWORD>(existingSetting.size()), g_state.settingsPath.c_str()) != 0 &&
        GetPrivateProfileStringW(L"Controls", L"SensitivityDecreaseKey", L"", existingSetting.data(),
            static_cast<DWORD>(existingSetting.size()), g_state.settingsPath.c_str()) != 0 &&
        GetPrivateProfileStringW(L"Controls", L"SensitivityDisplayKey", L"", existingSetting.data(),
            static_cast<DWORD>(existingSetting.size()), g_state.settingsPath.c_str()) != 0 &&
        GetPrivateProfileStringW(L"Controls", L"SensitivityIncreaseKey", L"", existingSetting.data(),
            static_cast<DWORD>(existingSetting.size()), g_state.settingsPath.c_str()) != 0;
    g_state.sensitivity = std::clamp(static_cast<int>(GetPrivateProfileIntW(
        L"Controls", L"Sensitivity", kDefaultSensitivity, g_state.settingsPath.c_str())),
        kMinimumSensitivity, kMaximumSensitivity);
    g_state.sensitivityDownKeyName = ReadKeySetting(
        L"SensitivityDecreaseKey", L"F6", VK_F6, g_state.sensitivityDownKey);
    g_state.sensitivityDisplayKeyName = ReadKeySetting(
        L"SensitivityDisplayKey", L"F7", VK_F7, g_state.sensitivityDisplayKey);
    g_state.sensitivityUpKeyName = ReadKeySetting(
        L"SensitivityIncreaseKey", L"F8", VK_F8, g_state.sensitivityUpKey);
    if (g_state.sensitivityDownKey == g_state.sensitivityDisplayKey ||
        g_state.sensitivityDownKey == g_state.sensitivityUpKey ||
        g_state.sensitivityDisplayKey == g_state.sensitivityUpKey)
    {
        g_state.sensitivityDownKey = VK_F6;
        g_state.sensitivityDisplayKey = VK_F7;
        g_state.sensitivityUpKey = VK_F8;
        g_state.sensitivityDownKeyName = L"F6";
        g_state.sensitivityDisplayKeyName = L"F7";
        g_state.sensitivityUpKeyName = L"F8";
    }
    if (!settingsComplete)
    {
        g_state.settingsDirty = true;
        g_state.sensitivityChangedTick = 0;
    }
}

void SaveSettingsIfDue(bool force = false)
{
    if (!g_state.settingsDirty || g_state.settingsPath.empty() ||
        (!force && GetTickCount64() - g_state.sensitivityChangedTick < kSettingsSaveDelayMs)) return;
    const std::wstring value = std::to_wstring(g_state.sensitivity);
    const bool saved = WritePrivateProfileStringW(L"Controls", L"Sensitivity", value.c_str(),
        g_state.settingsPath.c_str()) != FALSE &&
        WritePrivateProfileStringW(L"Controls", L"SensitivityDecreaseKey",
            g_state.sensitivityDownKeyName.c_str(), g_state.settingsPath.c_str()) != FALSE &&
        WritePrivateProfileStringW(L"Controls", L"SensitivityDisplayKey",
            g_state.sensitivityDisplayKeyName.c_str(), g_state.settingsPath.c_str()) != FALSE &&
        WritePrivateProfileStringW(L"Controls", L"SensitivityIncreaseKey",
            g_state.sensitivityUpKeyName.c_str(), g_state.settingsPath.c_str()) != FALSE;
    if (saved) g_state.settingsDirty = false;
}

void SetSensitivity(int sensitivity)
{
    const int next = std::clamp(sensitivity, kMinimumSensitivity, kMaximumSensitivity);
    if (next != g_state.sensitivity)
    {
        g_state.sensitivity = next;
        g_state.sensitivityChangedTick = GetTickCount64();
        g_state.settingsDirty = true;
    }
    ShowSensitivityOverlay();
}

LRESULT CALLBACK SensitivityOverlayWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_NCHITTEST) return HTTRANSPARENT;
    if (message == WM_ERASEBKGND) return 1;
    if (message == WM_TIMER && wParam == kSensitivityOverlayTimerId)
    {
        UpdateSensitivityOverlay();
        return 0;
    }
    if (message == WM_PAINT)
    {
        PAINTSTRUCT paint{};
        HDC device = BeginPaint(window, &paint);
        RECT client{};
        GetClientRect(window, &client);

        const HBRUSH background = CreateSolidBrush(RGB(17, 22, 18));
        const HBRUSH border = CreateSolidBrush(RGB(91, 105, 91));
        const HBRUSH track = CreateSolidBrush(RGB(55, 64, 56));
        const HBRUSH fill = CreateSolidBrush(RGB(189, 215, 99));
        const HRGN outerRegion = CreateRoundRectRgn(0, 0, client.right + 1, client.bottom + 1, 12, 12);
        FillRgn(device, outerRegion, border);
        const HRGN innerRegion = CreateRoundRectRgn(1, 1, client.right, client.bottom, 10, 10);
        FillRgn(device, innerRegion, background);

        SetBkMode(device, TRANSPARENT);
        SetTextColor(device, RGB(238, 241, 236));
        const HFONT labelFont = CreateFontW(-15, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
            DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
        const HFONT valueFont = CreateFontW(-17, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
            DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
        const HGDIOBJ oldFont = SelectObject(device, labelFont);
        RECT labelRect{ 20, 12, client.right - 70, 34 };
        DrawTextW(device, L"Scroll Wheel Movement Sensitivity", -1,
            &labelRect, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
        SelectObject(device, valueFont);
        const std::wstring value = std::to_wstring(g_state.sensitivity);
        RECT valueRect{ client.right - 66, 10, client.right - 20, 36 };
        DrawTextW(device, value.c_str(), -1, &valueRect, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);

        RECT trackRect{ 20, 47, client.right - 20, 57 };
        FillRect(device, &trackRect, track);
        const int trackWidth = trackRect.right - trackRect.left;
        RECT fillRect = trackRect;
        fillRect.right = fillRect.left + MulDiv(trackWidth, g_state.sensitivity, kMaximumSensitivity);
        if (fillRect.right > fillRect.left) FillRect(device, &fillRect, fill);

        SelectObject(device, oldFont);
        DeleteObject(valueFont);
        DeleteObject(labelFont);
        DeleteObject(innerRegion);
        DeleteObject(outerRegion);
        DeleteObject(fill);
        DeleteObject(track);
        DeleteObject(border);
        DeleteObject(background);
        EndPaint(window, &paint);
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

bool CreateSensitivityOverlay()
{
    WNDCLASSEXW windowClass{};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.lpfnWndProc = SensitivityOverlayWindowProc;
    windowClass.hInstance = g_state.module;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.lpszClassName = kSensitivityOverlayClassName;
    if (RegisterClassExW(&windowClass) == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        return false;

    g_state.sensitivityOverlay = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
        kSensitivityOverlayClassName, L"", WS_POPUP, 0, 0,
        kSensitivityOverlayWidth, kSensitivityOverlayHeight, nullptr, nullptr, g_state.module, nullptr);
    if (g_state.sensitivityOverlay == nullptr) return false;
    SetLayeredWindowAttributes(g_state.sensitivityOverlay, 0, kSensitivityOverlayOpacity, LWA_ALPHA);
    const HRGN roundedRegion = CreateRoundRectRgn(
        0, 0, kSensitivityOverlayWidth + 1, kSensitivityOverlayHeight + 1, 12, 12);
    SetWindowRgn(g_state.sensitivityOverlay, roundedRegion, FALSE);
    return true;
}

void DestroySensitivityOverlay()
{
    if (g_state.sensitivityOverlay != nullptr)
    {
        KillTimer(g_state.sensitivityOverlay, kSensitivityOverlayTimerId);
        DestroyWindow(g_state.sensitivityOverlay);
        g_state.sensitivityOverlay = nullptr;
    }
    UnregisterClassW(kSensitivityOverlayClassName, g_state.module);
}

void ShowSensitivityOverlay()
{
    if (g_state.sensitivityOverlay == nullptr) return;
    RECT gameRect{};
    if (g_state.gameWindow == nullptr || !GetWindowRect(g_state.gameWindow, &gameRect))
    {
        gameRect.left = 0;
        gameRect.top = 0;
        gameRect.right = GetSystemMetrics(SM_CXSCREEN);
    }
    const int left = gameRect.left + ((gameRect.right - gameRect.left) - kSensitivityOverlayWidth) / 2;
    const int top = gameRect.top + kSensitivityOverlayTopOffset;
    SetWindowPos(g_state.sensitivityOverlay, HWND_TOPMOST, left, top,
        kSensitivityOverlayWidth, kSensitivityOverlayHeight,
        SWP_NOACTIVATE | SWP_SHOWWINDOW);
    SetLayeredWindowAttributes(g_state.sensitivityOverlay, 0, kSensitivityOverlayOpacity, LWA_ALPHA);
    InvalidateRect(g_state.sensitivityOverlay, nullptr, FALSE);
    g_state.sensitivityOverlayFadeTick = GetTickCount64() + kSensitivityOverlayHoldMs;
    g_state.sensitivityOverlayHideTick = g_state.sensitivityOverlayFadeTick + kSensitivityOverlayFadeMs;
    SetTimer(g_state.sensitivityOverlay, kSensitivityOverlayTimerId, kSensitivityOverlayFrameMs, nullptr);
}

void UpdateSensitivityOverlay()
{
    if (g_state.sensitivityOverlay == nullptr || !IsWindowVisible(g_state.sensitivityOverlay)) return;
    const ULONGLONG now = GetTickCount64();
    if (now >= g_state.sensitivityOverlayHideTick)
    {
        KillTimer(g_state.sensitivityOverlay, kSensitivityOverlayTimerId);
        ShowWindow(g_state.sensitivityOverlay, SW_HIDE);
        return;
    }
    if (now >= g_state.sensitivityOverlayFadeTick)
    {
        const ULONGLONG remaining = g_state.sensitivityOverlayHideTick - now;
        const BYTE opacity = static_cast<BYTE>(std::max<ULONGLONG>(1,
            remaining * kSensitivityOverlayOpacity / kSensitivityOverlayFadeMs));
        SetLayeredWindowAttributes(g_state.sensitivityOverlay, 0, opacity, LWA_ALPHA);
    }
}

void PollSensitivityControls()
{
    if (!IsGameForeground())
    {
        g_state.sensitivityDownHeld = false;
        g_state.sensitivityDisplayHeld = false;
        g_state.sensitivityUpHeld = false;
        return;
    }

    const ULONGLONG now = GetTickCount64();
    const bool downPressed = (GetAsyncKeyState(g_state.sensitivityDownKey) & 0x8000) != 0;
    const bool displayPressed = (GetAsyncKeyState(g_state.sensitivityDisplayKey) & 0x8000) != 0;
    const bool upPressed = (GetAsyncKeyState(g_state.sensitivityUpKey) & 0x8000) != 0;
    const bool sensitivityKeyWasHeld = g_state.sensitivityDownHeld ||
        g_state.sensitivityDisplayHeld || g_state.sensitivityUpHeld;

    if (downPressed && (!g_state.sensitivityDownHeld || now >= g_state.sensitivityDownRepeatTick))
    {
        SetSensitivity(g_state.sensitivity - kSensitivityIncrement);
        g_state.sensitivityDownRepeatTick = now +
            (g_state.sensitivityDownHeld ? kSensitivityRepeatIntervalMs : kSensitivityRepeatDelayMs);
    }
    if (displayPressed && !g_state.sensitivityDisplayHeld)
        ShowSensitivityOverlay();
    if (upPressed && (!g_state.sensitivityUpHeld || now >= g_state.sensitivityUpRepeatTick))
    {
        SetSensitivity(g_state.sensitivity + kSensitivityIncrement);
        g_state.sensitivityUpRepeatTick = now +
            (g_state.sensitivityUpHeld ? kSensitivityRepeatIntervalMs : kSensitivityRepeatDelayMs);
    }

    g_state.sensitivityDownHeld = downPressed;
    g_state.sensitivityDisplayHeld = displayPressed;
    g_state.sensitivityUpHeld = upPressed;
    if (sensitivityKeyWasHeld && !downPressed && !displayPressed && !upPressed)
        SaveSettingsIfDue(true);
}

float SensitivityScale()
{
    const float position = static_cast<float>(g_state.sensitivity - kDefaultSensitivity) /
        static_cast<float>(kDefaultSensitivity);
    if (g_state.sensitivity <= kDefaultSensitivity) return std::pow(4.0f, position);
    return std::pow(kMaximumSensitivityScale, position);
}

float AdvanceWithinRange(float target, int direction, float minimum, float maximum,
    float requestedStep)
{
    const float length = maximum - minimum;
    const int intervals = std::max(1, static_cast<int>(std::ceil(
        length / requestedStep - kTargetEpsilon)));
    const float step = length / static_cast<float>(intervals);
    const float position = std::clamp((target - minimum) / step, 0.0f,
        static_cast<float>(intervals));

    if (direction > 0)
    {
        const int nextIndex = std::min(intervals,
            static_cast<int>(std::floor(position + kTargetEpsilon)) + 1);
        return minimum + step * static_cast<float>(nextIndex);
    }

    const int nextIndex = std::max(0,
        static_cast<int>(std::ceil(position - kTargetEpsilon)) - 1);
    return minimum + step * static_cast<float>(nextIndex);
}

float AdvanceTargetWithScale(float target, int direction, float sensitivityScale)
{
    if (direction > 0)
    {
        if (target < kVanillaWalkTarget - kTargetEpsilon)
            return AdvanceWithinRange(target, direction, kMinimumTarget, kVanillaWalkTarget,
                kLowWalkBaseStep * sensitivityScale);
        if (target < kMaximumWalkTarget - kTargetEpsilon)
            return AdvanceWithinRange(target, direction, kVanillaWalkTarget, kMaximumWalkTarget,
                kHighWalkBaseStep * sensitivityScale);
        if (target < kMinimumJogTarget - kTargetEpsilon)
            return kMinimumJogTarget;
        return AdvanceWithinRange(target, direction, kMinimumJogTarget, kMaximumJogTarget,
            kJogBaseStep * sensitivityScale);
    }

    if (target > kMinimumJogTarget + kTargetEpsilon)
        return AdvanceWithinRange(target, direction, kMinimumJogTarget, kMaximumJogTarget,
            kJogBaseStep * sensitivityScale);
    if (target > kMaximumWalkTarget + kTargetEpsilon)
        return kMaximumWalkTarget;
    if (target > kVanillaWalkTarget + kTargetEpsilon)
        return AdvanceWithinRange(target, direction, kVanillaWalkTarget, kMaximumWalkTarget,
            kHighWalkBaseStep * sensitivityScale);
    return AdvanceWithinRange(target, direction, kMinimumTarget, kVanillaWalkTarget,
        kLowWalkBaseStep * sensitivityScale);
}

float AdvanceTarget(float target, int direction)
{
    return AdvanceTargetWithScale(target, direction, SensitivityScale());
}

float WalkAdsForScale(float scale)
{
    const float position = std::clamp((scale - kWalkMinimumScale) /
        (kWalkMaximumScale - kWalkMinimumScale), 0.0f, 1.0f);
    return kWalkAdsMinimum + position * (kWalkAdsMaximum - kWalkAdsMinimum);
}

float CrouchWalkAdsForScale(float scale)
{
    const float position = std::clamp((scale - kWalkMinimumScale) /
        (kWalkMaximumScale - kWalkMinimumScale), 0.0f, 1.0f);
    return kCrouchWalkAdsMinimum + position * (kCrouchWalkAdsMaximum - kCrouchWalkAdsMinimum);
}

float StandingAdsForTarget(float target)
{
    if (target <= 0.60f)
    {
        constexpr float slope = (kStandingWalkAdsAtMaximum - kStandingWalkAdsAtVanillaWalk) / (0.60f - 0.35f);
        return kStandingWalkAdsAtVanillaWalk + (target - 0.35f) * slope;
    }
    if (target < 0.70f)
    {
        const float position = (target - 0.60f) / (0.70f - 0.60f);
        return kStandingWalkAdsAtMaximum + position *
            (kStandingJogAdsAtMinimum - kStandingWalkAdsAtMaximum);
    }
    constexpr float jogSlope = (kStandingJogAdsAtMidpoint - kStandingJogAdsAtMinimum) / (0.85f - 0.70f);
    return std::min(kStandingAdsCap, kStandingJogAdsAtMinimum + (target - 0.70f) * jogSlope);
}

void ApplySelectedScale(float scale, float appliedTarget)
{
    g_state.selectedScale = scale;
    *reinterpret_cast<volatile float*>(g_state.cave + kSelectedScaleOffset) = scale;
    const float standingAds = StandingAdsForTarget(appliedTarget);
    const float walkScale = !TargetIsJog(g_state.currentTarget) ?
        appliedTarget / kVanillaWalkTarget : kWalkMaximumScale;
    const float walkAds = g_state.crouched ? CrouchWalkAdsForScale(walkScale) : standingAds;
    const float jogAds = g_state.crouched ? kCrouchJogAds : standingAds;
    *reinterpret_cast<volatile float*>(g_state.cave + kWalkAdsSelectedOffset) = walkAds;
    *reinterpret_cast<volatile float*>(g_state.cave + kJogAdsSelectedOffset) = jogAds;
    *reinterpret_cast<volatile LONG*>(g_state.cave + kAdsModeJogOffset) =
        TargetIsJog(g_state.currentTarget) ? 1 : 0;
}

void ApplyCurrentAppliedTarget()
{
    const float nativeMagnitude = g_state.lastModeJog ? 1.0f : 0.35f;
    ApplySelectedScale(g_state.appliedTarget / nativeMagnitude, g_state.appliedTarget);
}

void StopTargetSmoothing()
{
    if (g_state.smoothingTimer != 0)
    {
        KillTimer(nullptr, g_state.smoothingTimer);
        g_state.smoothingTimer = 0;
    }
    g_state.smoothingActive = false;
    g_state.smoothingLastTick = 0;
}

void ApplyCurrentTargetImmediately()
{
    StopTargetSmoothing();
    g_state.appliedTarget = g_state.currentTarget;
    ApplyCurrentAppliedTarget();
}

void UpdateTargetSmoothing()
{
    if (!g_state.smoothingActive) return;
    const ULONGLONG now = GetTickCount64();
    const ULONGLONG elapsedMs = std::min(
        now - g_state.smoothingLastTick, kTargetSmoothingMaxElapsedMs);
    g_state.smoothingLastTick = now;
    const float remaining = g_state.currentTarget - g_state.appliedTarget;
    if (std::abs(remaining) <= kTargetEpsilon)
    {
        g_state.appliedTarget = g_state.currentTarget;
        StopTargetSmoothing();
        ApplyCurrentAppliedTarget();
        return;
    }

    // Complete a full-range change in a short, fixed amount of time. This keeps
    // large high-sensitivity target jumps responsive while retaining continuous
    // intermediate values and immediate redirection when the wheel reverses.
    const float fullRange = kMaximumJogTarget - kMinimumTarget;
    const float maximumChange = fullRange *
        (static_cast<float>(elapsedMs) / kTargetSmoothingFullRangeMs);
    if (maximumChange <= 0.0f) return;
    g_state.appliedTarget += std::clamp(remaining, -maximumChange, maximumChange);
    if (std::abs(g_state.currentTarget - g_state.appliedTarget) <= kTargetEpsilon)
    {
        g_state.appliedTarget = g_state.currentTarget;
        StopTargetSmoothing();
    }
    // Keep gait inference in wheel-rebase mode throughout the transition and
    // briefly after it finishes. The observed movement value itself lags behind
    // scale changes, so treating that transient as a native-mode sample causes
    // false gait rebases and visible yanks.
    g_state.lastWheelAdjustmentTick = now;
    g_state.gaitDetectionSuppressedUntil = now + kWheelGaitRebaseWindowMs;
    ApplyCurrentAppliedTarget();
}

void BeginTargetSmoothing()
{
    if (g_state.sensitivity <= kDefaultSensitivity)
    {
        ApplyCurrentTargetImmediately();
        return;
    }
    if (g_state.smoothingActive) return;
    g_state.smoothingLastTick = GetTickCount64();
    g_state.gaitDetectionSuppressedUntil =
        g_state.smoothingLastTick + kWheelGaitRebaseWindowMs;
    g_state.smoothingActive = true;
    if (g_state.smoothingTimer == 0)
        g_state.smoothingTimer = SetTimer(nullptr, kTargetSmoothingTimerId,
            kTargetSmoothingFrameMs, nullptr);
    if (g_state.smoothingTimer == 0)
    {
        ApplyCurrentTargetImmediately();
        return;
    }

    // Apply one small, proven default-sensitivity step immediately. The player
    // gets prompt feedback without exposing Wildlands to the full distant target
    // in a single write; the time-based ramp completes the rest.
    const float remaining = g_state.currentTarget - g_state.appliedTarget;
    if (std::abs(remaining) > kTargetEpsilon)
    {
        const int direction = remaining > 0.0f ? 1 : -1;
        const float next = AdvanceTargetWithScale(g_state.appliedTarget, direction, 1.0f);
        g_state.appliedTarget = direction > 0 ?
            std::min(next, g_state.currentTarget) : std::max(next, g_state.currentTarget);
        g_state.lastWheelAdjustmentTick = g_state.smoothingLastTick;
        g_state.gaitDetectionSuppressedUntil =
            g_state.smoothingLastTick + kWheelGaitRebaseWindowMs;
        ApplyCurrentAppliedTarget();
    }
}

bool CurrentModeIsJog()
{
    const float observed = *reinterpret_cast<volatile float*>(g_state.cave + kRawMagnitudeOffset);
    g_state.lastRawMagnitude = g_state.selectedScale > 0.001f ? observed / g_state.selectedScale : observed;
    if (g_state.lastRawMagnitude > 0.70f) return true;
    if (g_state.lastRawMagnitude > 0.01f) return false;
    return g_state.lastModeJog;
}

void ResetModeChangeCandidate()
{
    g_state.pendingModeJog = g_state.lastModeJog;
    g_state.pendingModeSamples = 0;
}

template<typename T>
bool TryReadValue(std::uintptr_t address, T& value)
{
    if (address < 0x10000) return false;
    MEMORY_BASIC_INFORMATION region{};
    if (VirtualQuery(reinterpret_cast<const void*>(address), &region, sizeof(region)) != sizeof(region) ||
        region.State != MEM_COMMIT || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return false;
    const auto regionStart = reinterpret_cast<std::uintptr_t>(region.BaseAddress);
    const auto regionEnd = regionStart + region.RegionSize;
    if (address < regionStart || address > regionEnd || sizeof(T) > regionEnd - address) return false;
    std::memcpy(&value, reinterpret_cast<const void*>(address), sizeof(T));
    return true;
}

void RefreshCrouchState()
{
    std::uintptr_t owner = 0;
    std::uintptr_t stateObject = 0;
    std::uint8_t primary = 0;
    std::uint8_t mirror = 0;
    if (!TryReadValue(reinterpret_cast<std::uintptr_t>(g_state.cave + kProbeOwnerOffset), owner) ||
        !TryReadValue(owner + 0x38, stateObject) ||
        !TryReadValue(stateObject + 0xB0, primary) ||
        !TryReadValue(stateObject + 0x330, mirror) || primary != mirror || primary > 1) return;
    const bool detected = primary == 1;
    if (g_state.stanceKnown && detected == g_state.crouched) return;
    g_state.stanceKnown = true;
    g_state.crouched = detected;
    if (g_state.modeKnown) ApplyCurrentAppliedTarget();
}

void SetAdsOverrideEnabled(bool enabled)
{
    *reinterpret_cast<volatile LONG*>(g_state.cave + kAdsModeJogOffset) = TargetIsJog(g_state.currentTarget) ? 1 : 0;
    if (g_state.adsOverrideEnabled == enabled) return;
    *reinterpret_cast<volatile LONG*>(g_state.cave + kAdsEnabledOffset) = enabled ? 1 : 0;
    g_state.adsOverrideEnabled = enabled;
}

bool EnsureModeKnown()
{
    if (g_state.modeKnown) return true;
    if (!IsMoving()) return false;
    g_state.lastModeJog = CurrentModeIsJog();
    g_state.modeKnown = true;
    if (!g_state.stationaryTargetPending)
        g_state.currentTarget = g_state.lastModeJog ? kMaximumJogTarget : kVanillaWalkTarget;
    else
        g_state.lastWheelAdjustmentTick = GetTickCount64();
    g_state.stationaryTargetPending = false;
    g_state.appliedTarget = g_state.currentTarget;
    ResetModeChangeCandidate();
    ApplyCurrentTargetImmediately();
    return true;
}

LRESULT CALLBACK MouseHook(int code, WPARAM wParam, LPARAM lParam)
{
    if (code >= 0 && wParam == WM_MOUSEWHEEL && IsGameForeground())
    {
        const auto* mouse = reinterpret_cast<const MSLLHOOKSTRUCT*>(lParam);
        const short delta = static_cast<short>(HIWORD(mouse->mouseData));
        if (delta != 0)
        {
            if (!g_state.shiftBypass && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
            {
                const bool moving = IsMoving();
                if (moving && !EnsureModeKnown()) return 1;
                const int direction = delta > 0 ? 1 : -1;
                const float next = AdvanceTarget(g_state.currentTarget, direction);
                if (std::abs(next - g_state.currentTarget) > kTargetEpsilon)
                {
                    g_state.currentTarget = next;
                    g_state.lastWheelAdjustmentTick = GetTickCount64();
                    ResetModeChangeCandidate();
                    if (moving)
                    {
                        g_state.stationaryTargetPending = false;
                        BeginTargetSmoothing();
                        if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) SetAdsOverrideEnabled(true);
                    }
                    else
                    {
                        StopTargetSmoothing();
                        g_state.appliedTarget = g_state.currentTarget;
                        g_state.stationaryTargetPending = true;
                    }
                }
            }
            return 1;
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

void PollGameplayState()
{
    SaveSettingsIfDue();
    PollSensitivityControls();
    if (!IsGameForeground()) return;
    const bool moving = IsMoving();
    const bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    if (shiftDown && moving)
    {
        if (!g_state.shiftBypass)
        {
            g_state.shiftBypass = true;
            StopTargetSmoothing();
            SetAdsOverrideEnabled(false);
            g_state.adsHeld = false;
            ApplySelectedScale(1.0f, kMaximumJogTarget);
        }
        return;
    }

    if (g_state.shiftBypass)
    {
        g_state.shiftBypass = false;
        g_state.currentTarget = kMaximumJogTarget;
        g_state.appliedTarget = kMaximumJogTarget;
        g_state.lastModeJog = true;
        g_state.modeKnown = true;
        ResetModeChangeCandidate();
        ApplyCurrentTargetImmediately();
        return;
    }

    if (!moving)
    {
        if (g_state.smoothingActive)
        {
            StopTargetSmoothing();
            g_state.appliedTarget = g_state.currentTarget;
        }
        ResetModeChangeCandidate();
        SetAdsOverrideEnabled(false);
        g_state.adsHeld = false;
        return;
    }
    if (!EnsureModeKnown()) return;
    if (g_state.stationaryTargetPending)
    {
        g_state.stationaryTargetPending = false;
        g_state.lastWheelAdjustmentTick = GetTickCount64();
        ResetModeChangeCandidate();
        g_state.appliedTarget = g_state.currentTarget;
        ApplyCurrentTargetImmediately();
    }
    RefreshCrouchState();
    const bool observedJog = CurrentModeIsJog();
    if (observedJog == g_state.lastModeJog)
    {
        ResetModeChangeCandidate();
    }
    else if (GetTickCount64() < g_state.gaitDetectionSuppressedUntil)
    {
        // The probe reflects Wildlands' lagging movement response as well as its
        // native gait. Do not infer or rebase the gait until the transition has
        // had time to settle.
        ResetModeChangeCandidate();
    }
    else if (GetTickCount64() - g_state.lastWheelAdjustmentTick <= kWheelGaitRebaseWindowMs)
    {
        // At very low speeds Wildlands may change its native gait in response to
        // the scale we just applied. Preserve the selected wheel target and rebase
        // its multiplier instead of mistaking that transition for a user toggle.
        const ULONGLONG now = GetTickCount64();
        g_state.lastModeJog = observedJog;
        g_state.lastWheelAdjustmentTick = now;
        ResetModeChangeCandidate();
        ApplyCurrentAppliedTarget();
    }
    else
    {
        if (g_state.pendingModeSamples == 0 || g_state.pendingModeJog != observedJog)
        {
            g_state.pendingModeJog = observedJog;
            g_state.pendingModeSamples = 1;
        }
        else
        {
            ++g_state.pendingModeSamples;
        }
        if (g_state.pendingModeSamples >= kModeChangeConfirmationSamples)
        {
            const bool targetJog = !TargetIsJog(g_state.currentTarget);
            g_state.lastModeJog = observedJog;
            g_state.currentTarget = targetJog ? kMaximumJogTarget : kVanillaWalkTarget;
            g_state.appliedTarget = g_state.currentTarget;
            ResetModeChangeCandidate();
            ApplyCurrentTargetImmediately();
        }
    }

    const bool adsNow = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    SetAdsOverrideEnabled(adsNow);
    if (adsNow && !g_state.adsHeld)
    {
        g_state.adsHeld = true;
        ApplyCurrentAppliedTarget();
    }
    else if (!adsNow && g_state.adsHeld)
    {
        g_state.adsHeld = false;
        ApplyCurrentAppliedTarget();
    }
}

void RestorePatches()
{
    if (!g_state.patchesInstalled) return;
    WriteCode(reinterpret_cast<void*>(g_state.adsSite), g_state.originalAds.data(), g_state.originalAds.size());
    WriteCode(reinterpret_cast<void*>(g_state.probeSite), kOriginalProbe.data(), kOriginalProbe.size());
    WriteCode(reinterpret_cast<void*>(g_state.controlSite), g_state.originalControl.data(), g_state.originalControl.size());
    g_state.patchesInstalled = false;
}

bool BuildTrampoline()
{
    constexpr std::size_t allocationSize = 8192;
    g_state.cave = static_cast<std::uint8_t*>(AllocateNear(g_state.controlSite, allocationSize));
    if (g_state.cave == nullptr) return false;
    std::memset(g_state.cave, 0, allocationSize);

    const std::uintptr_t caveAddress = reinterpret_cast<std::uintptr_t>(g_state.cave);
    const std::uintptr_t originalGetter = g_state.imageBase + g_state.layout->controlTargetRva;
    g_state.cave[0] = 0xE8;
    std::int32_t relative = 0;
    if (!Relative32(caveAddress, 5, originalGetter, relative)) return false;
    std::memcpy(g_state.cave + 1, &relative, sizeof(relative));
    const std::uint8_t controlCode[] = { 0xF3, 0x0F, 0x59, 0x05, 0xF7, 0x0F, 0x00, 0x00, 0xC3 };
    std::memcpy(g_state.cave + 5, controlCode, sizeof(controlCode));

    int probe = kProbeCodeOffset;
    auto append = [&](const void* bytes, int count)
    {
        std::memcpy(g_state.cave + probe, bytes, count);
        probe += count;
    };
    const std::uint8_t ownerOpcode[] = { 0x48, 0x89, 0x35 };
    append(ownerOpcode, sizeof(ownerOpcode));
    relative = static_cast<std::int32_t>((caveAddress + kProbeOwnerOffset) - (caveAddress + probe + 4));
    append(&relative, sizeof(relative));
    const std::uint8_t rawOpcode[] = { 0xF3, 0x0F, 0x11, 0x05 };
    append(rawOpcode, sizeof(rawOpcode));
    relative = static_cast<std::int32_t>((caveAddress + kRawMagnitudeOffset) - (caveAddress + probe + 4));
    append(&relative, sizeof(relative));
    append(kOriginalProbe.data(), static_cast<int>(kOriginalProbe.size()));
    const int jumpOffset = probe;
    const std::uint8_t jump = 0xE9;
    append(&jump, 1);
    if (!Relative32(caveAddress + jumpOffset, 5, g_state.probeSite + kOriginalProbe.size(), relative)) return false;
    append(&relative, sizeof(relative));

    int ads = kAdsCodeOffset;
    auto adsAppend = [&](const void* bytes, int count)
    {
        std::memcpy(g_state.cave + ads, bytes, count);
        ads += count;
    };
    auto adsRip = [&](const std::uint8_t* opcode, int opcodeLength, int dataOffset)
    {
        adsAppend(opcode, opcodeLength);
        const std::int32_t displacement = static_cast<std::int32_t>(
            (caveAddress + dataOffset) - (caveAddress + ads + 4));
        adsAppend(&displacement, sizeof(displacement));
    };
    const std::uint8_t storeOwner[] = { 0x48, 0x89, 0x0D };
    adsRip(storeOwner, sizeof(storeOwner), kAdsOwnerOffset);
    const std::uint8_t preserveStack[] = { 0x48, 0x83, 0xEC, 0x10, 0x0F, 0x11, 0x04, 0x24, 0x0F, 0x10, 0x44, 0x24, 0x58 };
    adsAppend(preserveStack, sizeof(preserveStack));
    const std::uint8_t storeVector[] = { 0x0F, 0x11, 0x05 };
    adsRip(storeVector, sizeof(storeVector), kAdsVectorOffset);
    const std::uint8_t restoreStackAndFlags[] = { 0x0F, 0x10, 0x04, 0x24, 0x48, 0x83, 0xC4, 0x10, 0x9C };
    adsAppend(restoreStackAndFlags, sizeof(restoreStackAndFlags));
    const std::uint8_t storeRawAds[] = { 0xF3, 0x0F, 0x11, 0x3D };
    adsRip(storeRawAds, sizeof(storeRawAds), kAdsRawOffset);
    const std::uint8_t compareEnabled[] = { 0x83, 0x3D };
    adsAppend(compareEnabled, sizeof(compareEnabled));
    relative = static_cast<std::int32_t>((caveAddress + kAdsEnabledOffset) - (caveAddress + ads + 5));
    adsAppend(&relative, sizeof(relative));
    const std::uint8_t enabledTail[] = { 0x00, 0x74, 27, 0x83, 0x3D };
    adsAppend(enabledTail, sizeof(enabledTail));
    relative = static_cast<std::int32_t>((caveAddress + kAdsModeJogOffset) - (caveAddress + ads + 5));
    adsAppend(&relative, sizeof(relative));
    const std::uint8_t modeTail[] = { 0x00, 0x75, 10 };
    adsAppend(modeTail, sizeof(modeTail));
    const std::uint8_t loadWalkAds[] = { 0xF3, 0x0F, 0x10, 0x3D };
    adsRip(loadWalkAds, sizeof(loadWalkAds), kWalkAdsSelectedOffset);
    const std::uint8_t skipJog[] = { 0xEB, 8 };
    adsAppend(skipJog, sizeof(skipJog));
    const std::uint8_t loadJogAds[] = { 0xF3, 0x0F, 0x10, 0x3D };
    adsRip(loadJogAds, sizeof(loadJogAds), kJogAdsSelectedOffset);
    const int adsJumpOffset = ads;
    const std::uint8_t restoreFlagsAndJump[] = { 0x9D, 0xE9 };
    adsAppend(restoreFlagsAndJump, sizeof(restoreFlagsAndJump));
    if (!Relative32(caveAddress + adsJumpOffset + 1, 5,
        g_state.imageBase + g_state.layout->adsTargetRva, relative)) return false;
    adsAppend(&relative, sizeof(relative));

    ApplySelectedScale(1.0f, kMaximumJogTarget);
    DWORD oldProtection = 0;
    if (!VirtualProtect(g_state.cave, 4096, PAGE_EXECUTE_READ, &oldProtection)) return false;

    g_state.controlRedirect = { 0xE8, 0, 0, 0, 0 };
    if (!Relative32(g_state.controlSite, g_state.controlRedirect.size(), caveAddress, relative)) return false;
    std::memcpy(g_state.controlRedirect.data() + 1, &relative, sizeof(relative));
    g_state.probeRedirect = { 0xE9, 0, 0, 0, 0 };
    if (!Relative32(g_state.probeSite, g_state.probeRedirect.size(), caveAddress + kProbeCodeOffset, relative)) return false;
    std::memcpy(g_state.probeRedirect.data() + 1, &relative, sizeof(relative));
    g_state.adsRedirect = { 0xE8, 0, 0, 0, 0 };
    if (!Relative32(g_state.adsSite, g_state.adsRedirect.size(), caveAddress + kAdsCodeOffset, relative)) return false;
    std::memcpy(g_state.adsRedirect.data() + 1, &relative, sizeof(relative));
    return true;
}

DWORD WINAPI RuntimeWorker(void*)
{
    const std::wstring hostPath = ModulePath(nullptr);
    if (_wcsicmp(FileName(hostPath).c_str(), L"GRW.exe") != 0) return 1;
    LoadSettings();
    g_state.imageBase = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (g_state.imageBase == 0) return 2;
    const std::size_t imageSize = ImageSize(g_state.imageBase);
    if (imageSize == 0) return 3;

    if (kLayout.controlSiteRva + 5 > imageSize ||
        kLayout.probeSiteRva + 5 > imageSize ||
        kLayout.adsSiteRva + 5 > imageSize ||
        kLayout.controlTargetRva >= imageSize ||
        kLayout.adsTargetRva >= imageSize) return 4;
    const auto expectedControl = OriginalControlBytes(g_state.imageBase, kLayout);
    const auto expectedAds = OriginalAdsBytes(g_state.imageBase, kLayout);
    const auto* control = reinterpret_cast<const std::uint8_t*>(g_state.imageBase + kLayout.controlSiteRva);
    const auto* probe = reinterpret_cast<const std::uint8_t*>(g_state.imageBase + kLayout.probeSiteRva);
    const auto* ads = reinterpret_cast<const std::uint8_t*>(g_state.imageBase + kLayout.adsSiteRva);
    if (std::memcmp(control, expectedControl.data(), expectedControl.size()) != 0 ||
        std::memcmp(probe, kOriginalProbe.data(), kOriginalProbe.size()) != 0 ||
        std::memcmp(ads, expectedAds.data(), expectedAds.size()) != 0) return 4;
    g_state.layout = &kLayout;
    g_state.originalControl = expectedControl;
    g_state.originalAds = expectedAds;
    g_state.controlSite = g_state.imageBase + g_state.layout->controlSiteRva;
    g_state.probeSite = g_state.imageBase + g_state.layout->probeSiteRva;
    g_state.adsSite = g_state.imageBase + g_state.layout->adsSiteRva;
    if (!BuildTrampoline())
    {
        if (g_state.cave != nullptr) VirtualFree(g_state.cave, 0, MEM_RELEASE);
        g_state.cave = nullptr;
        return 5;
    }

    if (!WriteCode(reinterpret_cast<void*>(g_state.controlSite), g_state.controlRedirect.data(), g_state.controlRedirect.size()))
    {
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 6;
    }
    g_state.patchesInstalled = true;
    if (!WriteCode(reinterpret_cast<void*>(g_state.probeSite), g_state.probeRedirect.data(), g_state.probeRedirect.size()))
    {
        RestorePatches();
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 7;
    }
    if (!WriteCode(reinterpret_cast<void*>(g_state.adsSite), g_state.adsRedirect.data(), g_state.adsRedirect.size()))
    {
        RestorePatches();
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 8;
    }
    if (std::memcmp(reinterpret_cast<const void*>(g_state.controlSite), g_state.controlRedirect.data(), g_state.controlRedirect.size()) != 0 ||
        std::memcmp(reinterpret_cast<const void*>(g_state.probeSite), g_state.probeRedirect.data(), g_state.probeRedirect.size()) != 0 ||
        std::memcmp(reinterpret_cast<const void*>(g_state.adsSite), g_state.adsRedirect.data(), g_state.adsRedirect.size()) != 0)
    {
        RestorePatches();
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 9;
    }

    int stableForegroundTicks = 0;
    while (stableForegroundTicks < 100)
    {
        stableForegroundTicks = IsGameForeground() ? stableForegroundTicks + 1 : 0;
        Sleep(100);
    }
    g_state.gameWindow = GetForegroundWindow();
    CreateSensitivityOverlay();
    const HMODULE hostModule = GetModuleHandleW(nullptr);
    g_state.mouseHook = SetWindowsHookExW(WH_MOUSE_LL, MouseHook, hostModule, 0);
    g_state.timer = SetTimer(nullptr, 1, 50, nullptr);
    if (g_state.mouseHook == nullptr || g_state.timer == 0)
    {
        if (g_state.timer != 0) KillTimer(nullptr, g_state.timer);
        if (g_state.mouseHook != nullptr) UnhookWindowsHookEx(g_state.mouseHook);
        DestroySensitivityOverlay();
        RestorePatches();
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 10;
    }

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0)
    {
        if (message.message == WM_TIMER && message.hwnd == nullptr)
        {
            if (message.wParam == g_state.timer) PollGameplayState();
            else if (message.wParam == g_state.smoothingTimer) UpdateTargetSmoothing();
        }
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    StopTargetSmoothing();
    KillTimer(nullptr, g_state.timer);
    UnhookWindowsHookEx(g_state.mouseHook);
    SaveSettingsIfDue(true);
    DestroySensitivityOverlay();
    RestorePatches();
    VirtualFree(g_state.cave, 0, MEM_RELEASE);
    g_state.cave = nullptr;
    return 0;
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(module);
    g_state.module = module;
    const HANDLE worker = CreateThread(nullptr, 0, RuntimeWorker, nullptr, 0, nullptr);
    if (worker != nullptr) CloseHandle(worker);
    return TRUE;
}
