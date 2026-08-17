#include <windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <string>

#pragma comment(lib, "user32.lib")

namespace
{
constexpr int kWalkLevelCount = 16;
constexpr int kJogLevelCount = 11;
constexpr int kVanillaWalkLevel = 8;
constexpr int kJogMinimumLevel = kWalkLevelCount;
constexpr int kJogMaximumLevel = kWalkLevelCount + kJogLevelCount - 1;
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

constexpr std::array<float, kWalkLevelCount + kJogLevelCount> MakeTargets()
{
    std::array<float, kWalkLevelCount + kJogLevelCount> targets{};
    for (int level = 0; level < 9; ++level) targets[level] = 0.05f + level * (0.30f / 8.0f);
    for (int level = 1; level <= 7; ++level) targets[8 + level] = 0.35f + level * (0.25f / 7.0f);
    for (int level = 0; level < kJogLevelCount; ++level) targets[kWalkLevelCount + level] = 0.70f + level * 0.03f;
    return targets;
}

constexpr auto kTargets = MakeTargets();
constexpr std::array<std::uint8_t, 5> kOriginalProbe{ 0xF3, 0x0F, 0x11, 0x46, 0x60 };

struct RuntimeState
{
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
    int currentLevel{kJogMaximumLevel};
    float selectedScale{1.0f};
    float lastRawMagnitude{};
    ULONGLONG lastWheelAdjustmentTick{};
    bool pendingModeJog{};
    int pendingModeSamples{};
    bool lastModeJog{true};
    bool modeKnown{};
    bool stationaryLevelPending{};
    bool shiftBypass{};
    bool adsHeld{};
    bool adsOverrideEnabled{};
    bool crouched{};
    bool stanceKnown{};
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

bool LevelIsJog(int level)
{
    return level >= kJogMinimumLevel;
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
    constexpr float jogSlope = (kStandingJogAdsAtMidpoint - kStandingJogAdsAtMinimum) / (0.85f - 0.70f);
    return std::min(kStandingAdsCap, kStandingJogAdsAtMinimum + (target - 0.70f) * jogSlope);
}

void ApplySelectedScale(float scale)
{
    g_state.selectedScale = scale;
    *reinterpret_cast<volatile float*>(g_state.cave + kSelectedScaleOffset) = scale;
    const float target = kTargets[g_state.currentLevel];
    const float standingAds = StandingAdsForTarget(target);
    const float walkScale = g_state.currentLevel < kWalkLevelCount ? target / 0.35f : kWalkMaximumScale;
    const float walkAds = g_state.crouched ? CrouchWalkAdsForScale(walkScale) : standingAds;
    const float jogAds = g_state.crouched ? kCrouchJogAds : standingAds;
    *reinterpret_cast<volatile float*>(g_state.cave + kWalkAdsSelectedOffset) = walkAds;
    *reinterpret_cast<volatile float*>(g_state.cave + kJogAdsSelectedOffset) = jogAds;
    *reinterpret_cast<volatile LONG*>(g_state.cave + kAdsModeJogOffset) = LevelIsJog(g_state.currentLevel) ? 1 : 0;
}

void ApplyCurrentLevel()
{
    const float nativeMagnitude = g_state.lastModeJog ? 1.0f : 0.35f;
    ApplySelectedScale(kTargets[g_state.currentLevel] / nativeMagnitude);
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
    if (g_state.modeKnown) ApplyCurrentLevel();
}

void SetAdsOverrideEnabled(bool enabled)
{
    *reinterpret_cast<volatile LONG*>(g_state.cave + kAdsModeJogOffset) = LevelIsJog(g_state.currentLevel) ? 1 : 0;
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
    if (!g_state.stationaryLevelPending)
        g_state.currentLevel = g_state.lastModeJog ? kJogMaximumLevel : kVanillaWalkLevel;
    else
        g_state.lastWheelAdjustmentTick = GetTickCount64();
    g_state.stationaryLevelPending = false;
    ResetModeChangeCandidate();
    ApplyCurrentLevel();
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
                const int next = std::clamp(g_state.currentLevel + direction, 0, static_cast<int>(kTargets.size()) - 1);
                if (next != g_state.currentLevel)
                {
                    g_state.currentLevel = next;
                    g_state.lastWheelAdjustmentTick = GetTickCount64();
                    ResetModeChangeCandidate();
                    if (moving)
                    {
                        g_state.stationaryLevelPending = false;
                        ApplyCurrentLevel();
                        if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) SetAdsOverrideEnabled(true);
                    }
                    else
                    {
                        g_state.stationaryLevelPending = true;
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
    if (!IsGameForeground()) return;
    const bool moving = IsMoving();
    const bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    if (shiftDown && moving)
    {
        if (!g_state.shiftBypass)
        {
            g_state.shiftBypass = true;
            SetAdsOverrideEnabled(false);
            g_state.adsHeld = false;
            ApplySelectedScale(1.0f);
        }
        return;
    }

    if (g_state.shiftBypass)
    {
        g_state.shiftBypass = false;
        g_state.currentLevel = kJogMaximumLevel;
        g_state.lastModeJog = true;
        g_state.modeKnown = true;
        ResetModeChangeCandidate();
        ApplyCurrentLevel();
        return;
    }

    if (!moving)
    {
        ResetModeChangeCandidate();
        SetAdsOverrideEnabled(false);
        g_state.adsHeld = false;
        return;
    }
    if (!EnsureModeKnown()) return;
    if (g_state.stationaryLevelPending)
    {
        g_state.stationaryLevelPending = false;
        g_state.lastWheelAdjustmentTick = GetTickCount64();
        ResetModeChangeCandidate();
        ApplyCurrentLevel();
    }
    RefreshCrouchState();
    const bool observedJog = CurrentModeIsJog();
    if (observedJog == g_state.lastModeJog)
    {
        ResetModeChangeCandidate();
    }
    else if (GetTickCount64() - g_state.lastWheelAdjustmentTick <= kWheelGaitRebaseWindowMs)
    {
        // At very low speeds Wildlands may change its native gait in response to
        // the scale we just applied. Preserve the selected wheel level and rebase
        // its multiplier instead of mistaking that transition for a user toggle.
        const ULONGLONG now = GetTickCount64();
        g_state.lastModeJog = observedJog;
        g_state.lastWheelAdjustmentTick = now;
        ResetModeChangeCandidate();
        ApplyCurrentLevel();
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
            const bool targetJog = !LevelIsJog(g_state.currentLevel);
            g_state.lastModeJog = observedJog;
            g_state.currentLevel = targetJog ? kJogMaximumLevel : kVanillaWalkLevel;
            ResetModeChangeCandidate();
            ApplyCurrentLevel();
        }
    }

    const bool adsNow = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    SetAdsOverrideEnabled(adsNow);
    if (adsNow && !g_state.adsHeld)
    {
        g_state.adsHeld = true;
        ApplyCurrentLevel();
    }
    else if (!adsNow && g_state.adsHeld)
    {
        g_state.adsHeld = false;
        ApplyCurrentLevel();
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

    ApplySelectedScale(1.0f);
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
    const HMODULE hostModule = GetModuleHandleW(nullptr);
    g_state.mouseHook = SetWindowsHookExW(WH_MOUSE_LL, MouseHook, hostModule, 0);
    g_state.timer = SetTimer(nullptr, 1, 50, nullptr);
    if (g_state.mouseHook == nullptr || g_state.timer == 0)
    {
        if (g_state.timer != 0) KillTimer(nullptr, g_state.timer);
        if (g_state.mouseHook != nullptr) UnhookWindowsHookEx(g_state.mouseHook);
        RestorePatches();
        VirtualFree(g_state.cave, 0, MEM_RELEASE);
        return 10;
    }

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0)
    {
        if (message.message == WM_TIMER) PollGameplayState();
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    KillTimer(nullptr, g_state.timer);
    UnhookWindowsHookEx(g_state.mouseHook);
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
    const HANDLE worker = CreateThread(nullptr, 0, RuntimeWorker, nullptr, 0, nullptr);
    if (worker != nullptr) CloseHandle(worker);
    return TRUE;
}
