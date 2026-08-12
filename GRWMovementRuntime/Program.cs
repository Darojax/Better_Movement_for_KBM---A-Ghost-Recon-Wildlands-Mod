using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

const long ControlSiteRva = 0x133CF7E2;
const long ProbeSiteRva = 0x133CFF19;
const long AdsSiteRva = 0x133CFDFF;
const long AdsTargetRva = 0xA0CF70;
const long AdsOutputWriterRva = 0x29D7175;
const uint ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020, ProcessQueryInformation = 0x0400;
const uint MemCommit = 0x1000, MemReserve = 0x2000, MemRelease = 0x8000;
const uint PageReadWrite = 0x04, PageExecuteRead = 0x20, PageExecuteReadWrite = 0x40;
const uint WmHotkey = 0x0312, VkF4 = 0x73, VkF6 = 0x75, VkF7 = 0x76, VkF8 = 0x77;
const int WhKeyboardLl = 13, WhMouseLl = 14;
const uint WmMouseWheel = 0x020A, WmTimer = 0x0113;
const uint WmSpeedWheel = 0x8001;
const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
const int VkShift = 0x10, VkLShift = 0xA0, VkRShift = 0xA1;
const int VkRightButton = 0x02;
const int VkWalkJogToggle = 0x58;
const float WalkMinimumScale = 1.0f / 7.0f, WalkMaximumScale = 12.0f / 7.0f;
const float JogMinimumScale = 0.70f, JogMaximumScale = 1.00f, JogStepScale = 0.03f;
const int WalkLevelCount = 16, JogLevelCount = 11;
const int VanillaWalkLevelIndex = 8, JogMinimumLevelIndex = WalkLevelCount, JogMaximumLevelIndex = WalkLevelCount + JogLevelCount - 1;
const float WalkAdsMinimum = 1.6875144f, WalkAdsMaximum = 3.375f;
const float JogAdsMaximum = 4.10f;
const float StandingWalkAdsAtVanillaWalk = 1.81f, StandingWalkAdsAtMaximum = 2.48f;
const float StandingJogAdsAtMinimum = 3.00f, StandingJogAdsAtMidpoint = 3.50f, StandingAdsCap = 3.40f;
const float CrouchWalkAdsMinimum = 0.84f, CrouchWalkAdsMaximum = 1.68f;
const float CrouchJogAds = 2.70f;

string? processIdArgument = args.FirstOrDefault(argument => argument.StartsWith("--pid=", StringComparison.OrdinalIgnoreCase));
Process? selectedGame = null;
if (processIdArgument is not null)
{
    if (!int.TryParse(processIdArgument.AsSpan("--pid=".Length), out int requestedProcessId))
        throw new InvalidOperationException("The requested GRW process ID is invalid.");
    try { selectedGame = Process.GetProcessById(requestedProcessId); }
    catch (ArgumentException) { return 0; }
}
else
{
    Process[] candidates = Process.GetProcessesByName("GRW");
    if (candidates.Length != 1)
    {
        foreach (Process candidate in candidates) candidate.Dispose();
        throw new InvalidOperationException("The target GRW process was not specified unambiguously.");
    }
    selectedGame = candidates[0];
}
using Process game = selectedGame;
bool calibrationMode = args.Contains("--calibrate", StringComparer.OrdinalIgnoreCase);
bool adsCalibrationMode = args.Contains("--ads-calibrate", StringComparer.OrdinalIgnoreCase);
bool weaponAdsProbeMode = args.Contains("--weapon-ads-probe", StringComparer.OrdinalIgnoreCase);
bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
string? shutdownEventName = args.FirstOrDefault(argument => argument.StartsWith("--shutdown-event=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1].Trim('"');
using EventWaitHandle? shutdownEvent = shutdownEventName is null ? null : EventWaitHandle.OpenExisting(shutdownEventName);
if (new[] { calibrationMode, adsCalibrationMode, weaponAdsProbeMode }.Count(enabled => enabled) > 1)
    throw new InvalidOperationException("Select only one calibration or probe mode at a time.");
ulong imageBase = unchecked((ulong)(game.MainModule?.BaseAddress.ToInt64() ?? 0));
ulong controlSite = imageBase + (ulong)ControlSiteRva;
ulong probeSite = imageBase + (ulong)ProbeSiteRva;
ulong adsSite = imageBase + (ulong)AdsSiteRva;
ulong adsTarget = imageBase + (ulong)AdsTargetRva;
ulong adsOutputWriterSite = imageBase + (ulong)AdsOutputWriterRva;
nint process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation, false, game.Id);
if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");

ulong originalGetter = imageBase + 0x7D3BC0;
byte[] originalControl = new byte[5];
originalControl[0] = 0xE8;
BitConverter.GetBytes(CheckedRelative(controlSite, 5, originalGetter)).CopyTo(originalControl, 1);
byte[] originalProbe = [0xF3, 0x0F, 0x11, 0x46, 0x60];
byte[] originalAds = [0xE8, .. BitConverter.GetBytes(CheckedRelative(adsSite, 5, adsTarget))];
byte[] originalAdsOutputWriter = [0xF3, 0x0F, 0x11, 0x51, 0x20];
byte[] currentControl = ReadExact(process, controlSite, originalControl.Length);
byte[] currentProbe = ReadExact(process, probeSite, originalProbe.Length);
byte[] currentAds = ReadExact(process, adsSite, originalAds.Length);
byte[] currentAdsOutputWriter = ReadExact(process, adsOutputWriterSite, originalAdsOutputWriter.Length);
if (args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"Control 0x{controlSite:X16}: {Convert.ToHexString(currentControl)}");
    Console.WriteLine($"Probe   0x{probeSite:X16}: {Convert.ToHexString(currentProbe)}");
    Console.WriteLine($"ADS     0x{adsSite:X16}: {Convert.ToHexString(currentAds)}");
    Console.WriteLine($"ADS out 0x{adsOutputWriterSite:X16}: {Convert.ToHexString(currentAdsOutputWriter)}");
    bool verified = currentControl.SequenceEqual(originalControl) && currentProbe.SequenceEqual(originalProbe) && currentAds.SequenceEqual(originalAds) && currentAdsOutputWriter.SequenceEqual(originalAdsOutputWriter);
    Console.WriteLine(verified ? "All four exact original instructions verified." : "Instructions do not match a supported build; no changes were made.");
    CloseHandle(process);
    return verified ? 0 : 3;
}
if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
{
    ulong allocation = 0;
    if (!currentControl.SequenceEqual(originalControl))
    {
        if (currentControl[0] != 0xE8) throw new InvalidOperationException($"Unknown control bytes: {Convert.ToHexString(currentControl)}");
        allocation = unchecked((ulong)((long)controlSite + 5 + BitConverter.ToInt32(currentControl, 1)));
        PatchCode(process, controlSite, originalControl);
    }
    if (!currentProbe.SequenceEqual(originalProbe)) PatchCode(process, probeSite, originalProbe);
    if (!currentAds.SequenceEqual(originalAds)) PatchCode(process, adsSite, originalAds);
    if (!currentAdsOutputWriter.SequenceEqual(originalAdsOutputWriter)) PatchCode(process, adsOutputWriterSite, originalAdsOutputWriter);
    if (allocation != 0) VirtualFreeEx(process, (nint)allocation, 0, MemRelease);
    Console.WriteLine("All movement, ADS, and diagnostic instructions are restored.");
    CloseHandle(process);
    return 0;
}
if (!currentControl.SequenceEqual(originalControl) || !currentProbe.SequenceEqual(originalProbe) || !currentAds.SequenceEqual(originalAds) || !currentAdsOutputWriter.SequenceEqual(originalAdsOutputWriter))
    throw new InvalidOperationException("One or more live instructions do not match the supported original build. Refusing to attach; no changes were made.");

string gameExecutable = game.MainModule?.FileName ?? throw new InvalidOperationException("Could not resolve GRW.exe path.");
string gameDirectory = Path.GetDirectoryName(gameExecutable) ?? throw new InvalidOperationException("Could not resolve the Wildlands directory.");
string[] eacProcesses = Process.GetProcesses().Where(p => p.ProcessName.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("eac", StringComparison.OrdinalIgnoreCase)).Select(p => p.ProcessName).Distinct().ToArray();
if (eacProcesses.Length != 0) throw new InvalidOperationException($"Easy Anti-Cheat appears active ({string.Join(", ", eacProcesses)}). Refusing to attach.");
if (!SayNoToEacAppearsInstalled(gameDirectory)) throw new InvalidOperationException("SayNoToEAC stub/backup layout was not detected. Refusing to attach.");
if (!FirewallBlocksProgram(gameExecutable)) Console.WriteLine("CAUTION: no enabled outbound block rule was found for GRW.exe. Offline isolation is strongly recommended.");
string acceptanceDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GRW Analogue Movement Mod");
string acceptanceFile = Path.Combine(acceptanceDirectory, "offline-risk-accepted-v1");
if (!File.Exists(acceptanceFile))
{
    Console.WriteLine("WARNING: This unofficial mod writes to GRW process memory and is for offline single-player use only.");
    Console.WriteLine("No configuration guarantees protection from sanctions, crashes, save loss, or other damage.");
    Console.Write("Type I UNDERSTAND to accept the risk: ");
    if (!string.Equals(Console.ReadLine(), "I UNDERSTAND", StringComparison.Ordinal)) throw new InvalidOperationException("Risk acknowledgement was not accepted.");
    Directory.CreateDirectory(acceptanceDirectory);
    File.WriteAllText(acceptanceFile, DateTimeOffset.UtcNow.ToString("O"));
}

ulong cave = 0;
bool installed = false, writerInstalled = false, restored = false;
try
{
    cave = AllocateNear(process, controlSite);
    const int ProbeCodeOffset = 64, AdsCodeOffset = 128, AdsOutputWriterCodeOffset = 512;
    const int RawMagnitudeOffset = 4096, SelectedScaleOffset = 4100, AdsRawOffset = 4104;
    const int WalkAdsSelectedOffset = 4108, JogAdsSelectedOffset = 4112;
    const int WalkAdsLowOffset = 4116, WalkAdsHighOffset = 4120, JogAdsLowOffset = 4124, JogAdsHighOffset = 4128;
    const int AdsEnabledOffset = 4132, AdsModeJogOffset = 4136, AdsVectorOffset = 4140;
    const int AdsOutputObjectOffset = 4156, AdsOutputValueOffset = 4164, AdsOwnerOffset = 4168, ProbeOwnerOffset = 4176;
    byte[] trampoline = new byte[4184];
    trampoline[0] = 0xE8; // call original magnitude getter
    BitConverter.GetBytes(CheckedRelative(cave, 5, originalGetter)).CopyTo(trampoline, 1);
    byte[] controlCode = [0xF3, 0x0F, 0x59, 0x05, 0xF7, 0x0F, 0x00, 0x00, 0xC3]; // mulss xmm0,[selected]; ret
    controlCode.CopyTo(trampoline, 5);
    int probe = ProbeCodeOffset;
    void ProbeBytes(params byte[] bytes) { bytes.CopyTo(trampoline, probe); probe += bytes.Length; }
    ProbeBytes(0x48, 0x89, 0x35);                         // mov [rip+owner],rsi
    ProbeBytes(BitConverter.GetBytes(checked((int)((long)(cave + ProbeOwnerOffset) - (long)(cave + (ulong)probe + 4)))));
    ProbeBytes(0xF3, 0x0F, 0x11, 0x05);                   // movss [rip+raw],xmm0
    ProbeBytes(BitConverter.GetBytes(checked((int)((long)(cave + RawMagnitudeOffset) - (long)(cave + (ulong)probe + 4)))));
    ProbeBytes(originalProbe);
    int probeReturnJumpOffset = probe;
    ProbeBytes(0xE9);
    ProbeBytes(BitConverter.GetBytes(CheckedRelative(cave + (ulong)probeReturnJumpOffset, 5, probeSite + (ulong)originalProbe.Length)));

    int ads = AdsCodeOffset;
    void AdsBytes(params byte[] bytes) { bytes.CopyTo(trampoline, ads); ads += bytes.Length; }
    void AdsRip(byte[] opcode, int dataOffset)
    {
        AdsBytes(opcode);
        int displacement = checked((int)((long)(cave + (ulong)dataOffset) - (long)(cave + (ulong)ads + 4)));
        AdsBytes(BitConverter.GetBytes(displacement));
    }
    AdsRip([0x48, 0x89, 0x0D], AdsOwnerOffset);             // snapshot player ADS owner (rcx)
    AdsBytes(0x48, 0x83, 0xEC, 0x10);                       // preserve xmm0 in a temporary stack slot
    AdsBytes(0x0F, 0x11, 0x04, 0x24);                       // movups [rsp],xmm0
    AdsBytes(0x0F, 0x10, 0x44, 0x24, 0x58);                 // caller [rsp+40] vector (account for CALL and temp slot)
    AdsRip([0x0F, 0x11, 0x05], AdsVectorOffset);             // snapshot the pre-ADS movement vector
    AdsBytes(0x0F, 0x10, 0x04, 0x24);                       // restore xmm0
    AdsBytes(0x48, 0x83, 0xC4, 0x10);
    AdsBytes(0x9C);                                           // pushfq: preserve caller flags
    AdsRip([0xF3, 0x0F, 0x11, 0x3D], AdsRawOffset);          // store original xmm7
    AdsBytes(0x83, 0x3D);                                    // cmp dword ptr [rip+enabled],0
    int enabledDisplacement = checked((int)((long)(cave + AdsEnabledOffset) - (long)(cave + (ulong)ads + 5)));
    AdsBytes(BitConverter.GetBytes(enabledDisplacement));     // include disp32 and trailing imm8 in RIP base
    AdsBytes(0x00);
    AdsBytes(0x74, 27);                                      // RMB not held -> restore flags
    AdsBytes(0x83, 0x3D);                                    // cmp dword ptr [rip+modeJog],0
    int modeDisplacement = checked((int)((long)(cave + AdsModeJogOffset) - (long)(cave + (ulong)ads + 5)));
    AdsBytes(BitConverter.GetBytes(modeDisplacement));
    AdsBytes(0x00);
    AdsBytes(0x75, 10);                                      // nonzero -> load jog ADS target
    AdsRip([0xF3, 0x0F, 0x10, 0x3D], WalkAdsSelectedOffset);
    AdsBytes(0xEB, 8);                                       // selected walk -> restore flags
    AdsRip([0xF3, 0x0F, 0x10, 0x3D], JogAdsSelectedOffset);
    AdsBytes(0x9D);                                           // popfq: original caller flags
    AdsBytes(0xE9);
    AdsBytes(BitConverter.GetBytes(CheckedRelative(cave + (ulong)ads - 1, 5, adsTarget)));

    int writer = AdsOutputWriterCodeOffset;
    originalAdsOutputWriter.CopyTo(trampoline, writer); writer += originalAdsOutputWriter.Length;
    trampoline[writer++] = 0x48; trampoline[writer++] = 0x89; trampoline[writer++] = 0x0D; // mov [rip+object],rcx
    BitConverter.GetBytes(checked((int)((long)(cave + AdsOutputObjectOffset) - (long)(cave + (ulong)writer + 4)))).CopyTo(trampoline, writer); writer += 4;
    trampoline[writer++] = 0xF3; trampoline[writer++] = 0x0F; trampoline[writer++] = 0x11; trampoline[writer++] = 0x15; // movss [rip+value],xmm2
    BitConverter.GetBytes(checked((int)((long)(cave + AdsOutputValueOffset) - (long)(cave + (ulong)writer + 4)))).CopyTo(trampoline, writer); writer += 4;
    trampoline[writer++] = 0xE9;
    BitConverter.GetBytes(CheckedRelative(cave + (ulong)writer - 1, 5, adsOutputWriterSite + (ulong)originalAdsOutputWriter.Length)).CopyTo(trampoline, writer);

    BitConverter.GetBytes(1.0f).CopyTo(trampoline, SelectedScaleOffset);
    BitConverter.GetBytes(WalkAdsMinimum).CopyTo(trampoline, WalkAdsSelectedOffset);
    BitConverter.GetBytes(JogAdsMaximum).CopyTo(trampoline, JogAdsSelectedOffset);
    BitConverter.GetBytes(1.25f).CopyTo(trampoline, WalkAdsLowOffset);
    BitConverter.GetBytes(1.45f).CopyTo(trampoline, WalkAdsHighOffset);
    BitConverter.GetBytes(2.35f).CopyTo(trampoline, JogAdsLowOffset);
    BitConverter.GetBytes(2.65f).CopyTo(trampoline, JogAdsHighOffset);
    BitConverter.GetBytes(1).CopyTo(trampoline, AdsModeJogOffset);
    WriteExact(process, cave, trampoline);
    if (!VirtualProtectEx(process, (nint)cave, 4096, PageExecuteRead, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());

    byte[] controlRedirect = new byte[5];
    controlRedirect[0] = 0xE8;
    BitConverter.GetBytes(CheckedRelative(controlSite, 5, cave)).CopyTo(controlRedirect, 1);
    byte[] probeRedirect = new byte[5];
    probeRedirect[0] = 0xE9;
    BitConverter.GetBytes(CheckedRelative(probeSite, 5, cave + ProbeCodeOffset)).CopyTo(probeRedirect, 1);
    byte[] adsRedirect = [0xE8, .. BitConverter.GetBytes(CheckedRelative(adsSite, 5, cave + AdsCodeOffset))];
    PatchCode(process, controlSite, controlRedirect);
    installed = true;
    PatchCode(process, probeSite, probeRedirect);
    PatchCode(process, adsSite, adsRedirect);
    if (!ReadExact(process, controlSite, 5).SequenceEqual(controlRedirect) || !ReadExact(process, probeSite, 5).SequenceEqual(probeRedirect) || !ReadExact(process, adsSite, 5).SequenceEqual(adsRedirect))
        throw new InvalidOperationException("Triple redirect verification failed.");
    if (weaponAdsProbeMode)
    {
        byte[] writerRedirect = new byte[5];
        writerRedirect[0] = 0xE9;
        BitConverter.GetBytes(CheckedRelative(adsOutputWriterSite, 5, cave + AdsOutputWriterCodeOffset)).CopyTo(writerRedirect, 1);
        PatchCode(process, adsOutputWriterSite, writerRedirect);
        writerInstalled = true;
        if (!ReadExact(process, adsOutputWriterSite, 5).SequenceEqual(writerRedirect))
            throw new InvalidOperationException("ADS output writer redirect verification failed.");
    }

    void Restore()
    {
        if (restored) return;
        restored = true;
        if (writerInstalled)
        {
            try { PatchCode(process, adsOutputWriterSite, originalAdsOutputWriter); } catch { }
            writerInstalled = false;
        }
        if (installed)
        {
            try { PatchCode(process, controlSite, originalControl); } catch { }
            try { PatchCode(process, probeSite, originalProbe); } catch { }
            try { PatchCode(process, adsSite, originalAds); } catch { }
            installed = false;
        }
    }

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Restore(); Environment.Exit(0); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
    if (!RegisterHotKey(0, 1, 0, VkF4))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey failed");
    if (calibrationMode && (!RegisterHotKey(0, 3, 0, VkF6) || !RegisterHotKey(0, 4, 0, VkF7)))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Calibration hotkey registration failed");
    if (adsCalibrationMode && (!RegisterHotKey(0, 5, 0, VkF8) || !RegisterHotKey(0, 8, 0, VkF6) || !RegisterHotKey(0, 9, 0, VkF7)))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "ADS calibration hotkey registration failed");
    if (weaponAdsProbeMode && (!RegisterHotKey(0, 6, 0, VkF6) || !RegisterHotKey(0, 7, 0, VkF7)))
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Weapon ADS probe hotkey registration failed");

    float[] unifiedTargets =
    [
        .. Enumerable.Range(0, 9).Select(level => 0.05f + level * (0.30f / 8.0f)),
        .. Enumerable.Range(1, 7).Select(level => 0.35f + level * (0.25f / 7.0f)),
        .. Enumerable.Range(0, JogLevelCount).Select(level => JogMinimumScale + level * JogStepScale)
    ];
    int currentLevelIndex = JogMaximumLevelIndex;
    float selectedScale = 1.0f, lastRawMagnitude = 0.0f;
    float? capturedWalkMaximum = null, capturedJogMinimum = null;
    float selectedJogAds = JogAdsMaximum;
    bool shiftBypass = false, lastModeJog = true, modeKnown = false, walkJogKeyDown = false;
    bool adsHeld = false, modeBeforeAdsJog = true;
    bool adsOverrideEnabled = false;
    bool crouched = false, stanceKnown = false;
    int weaponCaptureSequence = 0;
    string weaponCaptureDirectory = Path.Combine(AppContext.BaseDirectory, "weapon-probe-captures");
    (ulong Owner, ulong StateObject, byte Primary, byte Mirror) ReadStanceCandidates()
    {
        ulong owner = BitConverter.ToUInt64(ReadExact(process, cave + ProbeOwnerOffset, 8));
        if (owner < 0x10000) throw new InvalidOperationException("Movement owner is not available; move briefly before sampling.");
        ulong stateObject = BitConverter.ToUInt64(ReadExact(process, owner + 0x38, 8));
        if (stateObject < 0x10000) throw new InvalidOperationException("Stance-state object is not available.");
        byte primary = ReadExact(process, stateObject + 0xB0, 1)[0];
        byte mirror = ReadExact(process, stateObject + 0x330, 1)[0];
        return (owner, stateObject, primary, mirror);
    }
    bool TryReadCrouched(out bool value)
    {
        value = false;
        try
        {
            var sample = ReadStanceCandidates();
            if (sample.Primary != sample.Mirror || sample.Primary > 1) return false;
            value = sample.Primary == 1;
            return true;
        }
        catch { return false; }
    }
    bool IsGameForeground()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return false;
        GetWindowThreadProcessId(foreground, out uint foregroundPid);
        return foregroundPid == (uint)game.Id;
    }
    bool IsMoving() => new[] { 0x57, 0x41, 0x53, 0x44 }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    bool IsAdsHeld() => (GetAsyncKeyState(VkRightButton) & 0x8000) != 0;
    bool IsShiftKey(int key) => key is VkShift or VkLShift or VkRShift;
    bool CurrentModeIsJog()
    {
        float observed = ReadFloat(process, cave + RawMagnitudeOffset);
        lastRawMagnitude = selectedScale > 0.001f ? observed / selectedScale : observed;
        if (lastRawMagnitude > 0.70f) return true;
        if (lastRawMagnitude > 0.01f) return false;
        return lastModeJog;
    }
    float WalkAdsForScale(float scale)
    {
        float position = Math.Clamp((scale - WalkMinimumScale) / (WalkMaximumScale - WalkMinimumScale), 0.0f, 1.0f);
        return WalkAdsMinimum + position * (WalkAdsMaximum - WalkAdsMinimum);
    }
    float CrouchWalkAdsForScale(float scale)
    {
        float position = Math.Clamp((scale - WalkMinimumScale) / (WalkMaximumScale - WalkMinimumScale), 0.0f, 1.0f);
        return CrouchWalkAdsMinimum + position * (CrouchWalkAdsMaximum - CrouchWalkAdsMinimum);
    }
    float StandingAdsForTarget(float target)
    {
        if (target <= 0.60f)
        {
            const float slope = (StandingWalkAdsAtMaximum - StandingWalkAdsAtVanillaWalk) / (0.60f - 0.35f);
            return StandingWalkAdsAtVanillaWalk + (target - 0.35f) * slope;
        }
        const float jogSlope = (StandingJogAdsAtMidpoint - StandingJogAdsAtMinimum) / (0.85f - 0.70f);
        float matched = StandingJogAdsAtMinimum + (target - 0.70f) * jogSlope;
        return Math.Min(matched, StandingAdsCap);
    }
    void ApplySelectedScale(float value)
    {
        selectedScale = value;
        WriteScale(process, cave + SelectedScaleOffset, selectedScale);
        float standingAds = StandingAdsForTarget(unifiedTargets[currentLevelIndex]);
        float walkScale = currentLevelIndex < WalkLevelCount ? unifiedTargets[currentLevelIndex] / 0.35f : WalkMaximumScale;
        float walkAds = crouched ? CrouchWalkAdsForScale(walkScale) : standingAds;
        float jogAds = adsCalibrationMode ? selectedJogAds : crouched ? CrouchJogAds : standingAds;
        WriteScale(process, cave + WalkAdsSelectedOffset, walkAds);
        WriteScale(process, cave + JogAdsSelectedOffset, jogAds);
    }
    bool LevelIsJog(int levelIndex) => levelIndex >= JogMinimumLevelIndex;
    float NativeGaitMagnitude() => lastModeJog ? 1.0f : 0.35f;
    void ApplyCurrentLevel() => ApplySelectedScale(unifiedTargets[currentLevelIndex] / NativeGaitMagnitude());
    void PrintLiveLevel(string reason)
    {
        if (!verbose) return;
        Console.WriteLine($"LIVE {reason,-12} | level {currentLevelIndex + 1,2}/{unifiedTargets.Length} | {(LevelIsJog(currentLevelIndex) ? "jog " : "walk")} | HIP target {unifiedTargets[currentLevelIndex]:0.0000} | native {(lastModeJog ? "jog " : "walk")} | multiplier {selectedScale:0.0000} | stance {(crouched ? "crouched" : "standing")}");
    }
    void RefreshCrouchState()
    {
        if (!TryReadCrouched(out bool detected)) return;
        if (stanceKnown && crouched == detected) return;
        bool changed = stanceKnown;
        stanceKnown = true;
        crouched = detected;
        if (modeKnown) ApplyCurrentLevel();
        Console.WriteLine(changed
            ? $"Stance changed: {(crouched ? "crouched" : "standing")}; movement coefficients refreshed."
            : $"Stance detected: {(crouched ? "crouched" : "standing")}.");
        if (modeKnown) PrintLiveLevel("stance");
    }
    void SetAdsOverrideEnabled(bool enabled)
    {
        WriteExact(process, cave + AdsModeJogOffset, BitConverter.GetBytes(LevelIsJog(currentLevelIndex) ? 1 : 0));
        if (adsOverrideEnabled == enabled) return;
        WriteExact(process, cave + AdsEnabledOffset, BitConverter.GetBytes(enabled ? 1 : 0));
        adsOverrideEnabled = enabled;
    }
    ApplySelectedScale(selectedScale);
    bool EnsureModeKnown()
    {
        if (modeKnown) return true;
        if (!IsMoving()) return false;
        lastModeJog = CurrentModeIsJog();
        modeKnown = true;
        currentLevelIndex = lastModeJog ? JogMaximumLevelIndex : VanillaWalkLevelIndex;
        if (adsHeld || IsAdsHeld()) modeBeforeAdsJog = LevelIsJog(currentLevelIndex);
        if (TryReadCrouched(out bool detectedCrouch))
        {
            crouched = detectedCrouch;
            stanceKnown = true;
        }
        ApplyCurrentLevel();
        Console.WriteLine($"Native gait detected as {(lastModeJog ? "jog" : "walk")}; unified level {currentLevelIndex + 1}/{unifiedTargets.Length} initialized.");
        PrintLiveLevel("initialized");
        return true;
    }
    string CaptureWeaponObjectGraph(string weapon, ulong rootAddress)
    {
        Directory.CreateDirectory(weaponCaptureDirectory);
        string stem = $"{++weaponCaptureSequence:D2}-{weapon.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmssfff}";
        List<string> manifest = ["node\tdepth\tpath\taddress\tsize\tfile"];
        HashSet<ulong> captured = [rootAddress];
        Queue<(ulong Address, string Path, int Depth, int RequestedSize)> pending = [];
        pending.Enqueue((rootAddress, "root", 0, 0x1000));
        int node = 0;
        while (pending.Count != 0 && node < 512)
        {
            (ulong address, string path, int depth, int requestedSize) = pending.Dequeue();
            if (!TryReadExact(process, address, requestedSize, out byte[] bytes))
            {
                if (requestedSize <= 0x400 || !TryReadExact(process, address, 0x400, out bytes)) continue;
            }
            string nodeName = $"{stem}-node-{node:D4}.bin";
            File.WriteAllBytes(Path.Combine(weaponCaptureDirectory, nodeName), bytes);
            manifest.Add($"{node}\t{depth}\t{path}\t0x{address:X16}\t0x{bytes.Length:X}\t{nodeName}");
            node++;
            if (depth >= 2) continue;
            for (int offset = 0; offset <= bytes.Length - 8; offset += 8)
            {
                ulong pointer = BitConverter.ToUInt64(bytes, offset);
                bool plausibleHeapPointer = pointer is >= 0x10000 and < 0x0000700000000000 && (pointer & 7) == 0;
                if (!plausibleHeapPointer || !captured.Add(pointer)) continue;
                pending.Enqueue((pointer, path + $"/{offset:X3}", depth + 1, depth == 0 ? 0x800 : 0x400));
            }
        }
        bool TryPointer(ulong address, int offset, out ulong pointer)
        {
            pointer = 0;
            if (!TryReadExact(process, address + (ulong)offset, 8, out byte[] bytes)) return false;
            pointer = BitConverter.ToUInt64(bytes, 0);
            return pointer is >= 0x10000 and < 0x0000700000000000 && (pointer & 7) == 0;
        }
        if (TryPointer(rootAddress, 0x50, out ulong level1) &&
            TryPointer(level1, 0x138, out ulong level2) &&
            TryPointer(level2, 0xD0, out ulong fingerprint) &&
            (TryReadExact(process, fingerprint, 0x1000, out byte[] fingerprintBytes) ||
             TryReadExact(process, fingerprint, 0x400, out fingerprintBytes)))
        {
            string fingerprintName = stem + "-weapon-fingerprint.bin";
            File.WriteAllBytes(Path.Combine(weaponCaptureDirectory, fingerprintName), fingerprintBytes);
            manifest.Add($"fingerprint\t3\troot/050/138/0D0\t0x{fingerprint:X16}\t0x{fingerprintBytes.Length:X}\t{fingerprintName}");
        }
        string manifestPath = Path.Combine(weaponCaptureDirectory, stem + "-manifest.txt");
        File.WriteAllLines(manifestPath, manifest);
        return manifestPath;
    }
    uint helperThreadId = GetCurrentThreadId();
    LowLevelMouseProc mouseProc = (code, wParam, lParam) =>
    {
        if (code >= 0 && unchecked((uint)wParam.ToInt64()) == WmMouseWheel && IsGameForeground())
        {
            uint mouseData = unchecked((uint)Marshal.ReadInt32(lParam, 8));
            short delta = unchecked((short)(mouseData >> 16));
            if (delta != 0)
            {
                if (IsMoving()) PostThreadMessage(helperThreadId, WmSpeedWheel, delta > 0 ? 1U : 0U, 0);
                return 1; // reserve the wheel for the mod while Ghost Recon Wildlands has focus
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    };
    nint mouseHook = SetWindowsHookEx(WhMouseLl, mouseProc, GetModuleHandle(null), 0);
    if (mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed");
    LowLevelKeyboardProc keyboardProc = (code, wParam, lParam) =>
    {
        if (code >= 0)
        {
            uint message = unchecked((uint)wParam.ToInt64());
            int key = Marshal.ReadInt32(lParam);
            bool keyDown = message is WmKeyDown or WmSysKeyDown;
            bool keyUp = message is WmKeyUp or WmSysKeyUp;
            try
            {
                if (key == VkWalkJogToggle && IsGameForeground())
                {
                    if (keyDown)
                    {
                        if (!modeKnown && IsMoving()) EnsureModeKnown();
                        if (modeKnown)
                        {
                            if (!walkJogKeyDown)
                            {
                                walkJogKeyDown = true;
                                bool targetJog = !LevelIsJog(currentLevelIndex);
                                currentLevelIndex = targetJog ? JogMaximumLevelIndex : VanillaWalkLevelIndex;
                                ApplyCurrentLevel();
                                PrintLiveLevel("X anchor");
                                if (adsHeld || IsAdsHeld()) SetAdsOverrideEnabled(true);
                                Console.WriteLine($"X: selected vanilla {(targetJog ? "jog" : "walk")} speed.");
                            }
                            return 1; // emulate the visible toggle without changing Wildlands' hidden gait state
                        }
                    }
                    if (keyUp && walkJogKeyDown)
                    {
                        walkJogKeyDown = false;
                        return 1;
                    }
                }
                bool beginSprint = (IsShiftKey(key) && keyDown && IsMoving()) ||
                                   (key is 0x57 or 0x41 or 0x53 or 0x44 && keyDown && (GetAsyncKeyState(VkShift) & 0x8000) != 0);
                if (beginSprint && IsGameForeground() && !shiftBypass)
                {
                    shiftBypass = true;
                    ApplySelectedScale(1.0f);
                    Console.WriteLine("Sprint bypass active.");
                }
                if (IsShiftKey(key) && keyUp && shiftBypass)
                {
                    shiftBypass = false;
                    currentLevelIndex = JogMaximumLevelIndex;
                    lastModeJog = true;
                    modeKnown = true;
                    ApplyCurrentLevel();
                    PrintLiveLevel("sprint reset");
                    Console.WriteLine("Sprint released: returned to vanilla full jog.");
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    };
    nint keyboardHook = SetWindowsKeyboardHookEx(WhKeyboardLl, keyboardProc, GetModuleHandle(null), 0);
    if (keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard SetWindowsHookEx failed");
    nuint timer = SetTimer(0, 1, 50, 0);
    if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTimer failed");

    Console.WriteLine($"Verified movement and ADS hooks; trampoline at 0x{cave:X16}.");
    Console.WriteLine(calibrationMode
        ? "CALIBRATION MODE: remain in native jog; wheel step is 0.01. F6 captures maximum-walk target; F7 captures minimum-jog target."
        : adsCalibrationMode
            ? $"ADS CALIBRATION: global jog-ADS starts at {JogAdsMaximum:0.00}; F6 lowers 0.10, F7 raises 0.10, F8 records the preferred value."
            : $"Unified ladder: {WalkLevelCount} walk positions from 0.05-0.60 and {JogLevelCount} jog positions from 0.70-1.00; the wheel crosses both ranges automatically.");
    Console.WriteLine("X jumps from the current range to the opposite vanilla gait; no separate custom profiles are saved.");
    Console.WriteLine("Shift bypasses scaling for sprint; release returns to vanilla full jog.");
    Console.WriteLine("F4 resets the current range to its vanilla anchor.");
    if (weaponAdsProbeMode) Console.WriteLine("WEAPON ADS PROBE: while holding ADS and moving, F6 captures Stoner and F7 captures pistol.");
    while (GetMessage(out Msg message, 0, 0, 0) > 0)
    {
        if (message.Message == WmSpeedWheel)
        {
            if (shiftBypass || (GetAsyncKeyState(VkShift) & 0x8000) != 0) continue;
            bool increase = message.WParam != 0;
            if (calibrationMode)
            {
                selectedScale = Math.Clamp(MathF.Round(selectedScale + (increase ? 0.01f : -0.01f), 2), 0.10f, JogMaximumScale);
                WriteScale(process, cave + SelectedScaleOffset, selectedScale);
                continue;
            }
            if (!EnsureModeKnown()) continue;
            RefreshCrouchState();
            if (IsAdsHeld()) SetAdsOverrideEnabled(true);
            int nextLevel = Math.Clamp(currentLevelIndex + (increase ? 1 : -1), 0, unifiedTargets.Length - 1);
            if (nextLevel == currentLevelIndex) continue;
            currentLevelIndex = nextLevel;
            ApplyCurrentLevel();
            PrintLiveLevel(increase ? "wheel up" : "wheel down");
            continue;
        }
        if (message.Message == WmTimer && shutdownEvent?.WaitOne(0) == true)
        {
            Console.WriteLine("Launcher requested a clean runtime shutdown.");
            break;
        }
        if (message.Message == WmTimer && !shiftBypass)
        {
            if (game.HasExited) break;
            if (modeKnown) RefreshCrouchState();
            bool adsNow = IsGameForeground() && IsAdsHeld() && IsMoving();
            if (adsNow)
            {
                if (!EnsureModeKnown()) continue;
                RefreshCrouchState();
                SetAdsOverrideEnabled(true);
                if (!adsHeld)
                {
                    adsHeld = true;
                    modeBeforeAdsJog = LevelIsJog(currentLevelIndex);
                    ApplyCurrentLevel();
                    Console.WriteLine($"ADS entered in {(modeBeforeAdsJog ? "jog" : "walk")} range.");
                }
                continue;
            }
            SetAdsOverrideEnabled(false);
            if (adsHeld)
            {
                adsHeld = false;
                ApplyCurrentLevel();
                Console.WriteLine("ADS released; retained unified movement level.");
                continue;
            }
            if (!modeKnown && IsMoving()) EnsureModeKnown();
            if (modeKnown) RefreshCrouchState();
            continue;
        }
        if (message.Message != WmHotkey) continue;
        if (weaponAdsProbeMode && message.WParam is 6 or 7)
        {
            string weapon = message.WParam == 6 ? "Stoner" : "Pistol";
            float originalAdsScalar = ReadFloat(process, cave + AdsRawOffset);
            float selectedAdsScalar = LevelIsJog(currentLevelIndex) ? selectedJogAds : WalkAdsForScale(unifiedTargets[currentLevelIndex] / 0.35f);
            byte[] vectorBytes = ReadExact(process, cave + AdsVectorOffset, 16);
            float vectorX = BitConverter.ToSingle(vectorBytes, 0), vectorY = BitConverter.ToSingle(vectorBytes, 4);
            float vectorZ = BitConverter.ToSingle(vectorBytes, 8), vectorW = BitConverter.ToSingle(vectorBytes, 12);
            float vectorMagnitude = MathF.Sqrt(vectorX * vectorX + vectorY * vectorY + vectorZ * vectorZ);
            ulong outputObject = BitConverter.ToUInt64(ReadExact(process, cave + AdsOutputObjectOffset, 8));
            float outputValue = ReadFloat(process, cave + AdsOutputValueOffset);
            ulong adsOwner = BitConverter.ToUInt64(ReadExact(process, cave + AdsOwnerOffset, 8));
            string manifestPath = CaptureWeaponObjectGraph(weapon + "-adsowner", adsOwner);
            Console.WriteLine($"WEAPON_CAPTURE weapon={weapon} original_ads={originalAdsScalar:R} selected_ads={selectedAdsScalar:R} vector=({vectorX:R},{vectorY:R},{vectorZ:R},{vectorW:R}) vector_magnitude={vectorMagnitude:R} ads_owner=0x{adsOwner:X16} output_object=0x{outputObject:X16} output_value={outputValue:R} level={currentLevelIndex + 1}/{unifiedTargets.Length} range={(LevelIsJog(currentLevelIndex) ? "jog" : "walk")} native_gait={(lastModeJog ? "jog" : "walk")} rmb={IsAdsHeld()} moving={IsMoving()} manifest={manifestPath}");
            continue;
        }
        if (calibrationMode && message.WParam is 3 or 4)
        {
            bool confirmedJog = CurrentModeIsJog() && lastRawMagnitude > 0.70f;
            if (!confirmedJog)
            {
                Console.WriteLine(message.WParam == 3 ? "F6 capture refused: native jog was not active." : "F7 capture refused: native jog was not active.");
                continue;
            }
            if (message.WParam == 3)
            {
                capturedWalkMaximum = selectedScale;
                Console.WriteLine($"CAPTURED maximum-walk target: {capturedWalkMaximum:0.00} (derived walk multiplier {capturedWalkMaximum / 0.35f:0.000}).");
            }
            else
            {
                capturedJogMinimum = selectedScale;
                Console.WriteLine($"CAPTURED minimum-jog target: {capturedJogMinimum:0.00}.");
            }
            if (capturedWalkMaximum.HasValue && capturedJogMinimum.HasValue)
                Console.WriteLine($"CALIBRATION COMPLETE: walkMax={capturedWalkMaximum:0.00}; jogMin={capturedJogMinimum:0.00}; gap={capturedJogMinimum - capturedWalkMaximum:0.00}.");
            continue;
        }
        if (adsCalibrationMode && message.WParam is 8 or 9)
        {
            selectedJogAds = Math.Clamp(MathF.Round(selectedJogAds + (message.WParam == 9 ? 0.10f : -0.10f), 2), 2.00f, JogAdsMaximum);
            WriteScale(process, cave + JogAdsSelectedOffset, selectedJogAds);
            Console.WriteLine($"Global jog-ADS coefficient selected: {selectedJogAds:0.00}");
            continue;
        }
        if (adsCalibrationMode && message.WParam == 5)
        {
            Console.WriteLine($"CAPTURED preferred global jog-ADS coefficient: {selectedJogAds:0.00}.");
            continue;
        }
        if (message.WParam == 1)
        {
            if (!IsGameForeground()) continue;
            if (!EnsureModeKnown()) continue;
            currentLevelIndex = LevelIsJog(currentLevelIndex) ? JogMaximumLevelIndex : VanillaWalkLevelIndex;
            ApplyCurrentLevel();
            PrintLiveLevel("F4 anchor");
            Console.WriteLine($"F4: reset to vanilla {(LevelIsJog(currentLevelIndex) ? "jog" : "walk")} speed.");
        }
    }
    KillTimer(0, timer);
    UnhookWindowsHookEx(keyboardHook);
    UnhookWindowsHookEx(mouseHook);
    UnregisterHotKey(0, 1);
    if (calibrationMode) { UnregisterHotKey(0, 3); UnregisterHotKey(0, 4); }
    if (adsCalibrationMode) { UnregisterHotKey(0, 5); UnregisterHotKey(0, 8); UnregisterHotKey(0, 9); }
    if (weaponAdsProbeMode) { UnregisterHotKey(0, 6); UnregisterHotKey(0, 7); }
    Restore();
    Console.WriteLine("Original CALL restored; prototype detached.");
}
finally
{
    if (writerInstalled) try { PatchCode(process, adsOutputWriterSite, originalAdsOutputWriter); } catch { }
    if (installed)
    {
        try { PatchCode(process, controlSite, originalControl); } catch { }
        try { PatchCode(process, probeSite, originalProbe); } catch { }
        try { PatchCode(process, adsSite, originalAds); } catch { }
    }
    if (cave != 0) VirtualFreeEx(process, (nint)cave, 0, MemRelease);
    CloseHandle(process);
}

return 0;

static int CheckedRelative(ulong instruction, int length, ulong target)
{
    long relative = unchecked((long)target - ((long)instruction + length));
    if (relative is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("Allocated code is outside rel32 range.");
    return (int)relative;
}

static ulong AllocateNear(nint process, ulong site)
{
    const ulong Granularity = 0x10000, Range = 0x70000000;
    ulong low = (site > Range ? site - Range : Granularity) & ~(Granularity - 1);
    ulong high = (site + Range) & ~(Granularity - 1);
    for (ulong distance = Granularity; distance < Range; distance += Granularity)
    {
        foreach (ulong hint in new[] { (site + distance) & ~(Granularity - 1), (site - distance) & ~(Granularity - 1) })
        {
            if (hint < low || hint > high) continue;
            nint allocation = VirtualAllocEx(process, (nint)hint, 8192, MemCommit | MemReserve, PageReadWrite);
            if (allocation != 0) return unchecked((ulong)allocation.ToInt64());
        }
    }
    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate a nearby trampoline page.");
}

static void WriteScale(nint process, ulong address, float value)
{
    if (!VirtualProtectEx(process, (nint)address, 4, PageExecuteReadWrite, out uint old)) throw new Win32Exception(Marshal.GetLastWin32Error());
    try { WriteExact(process, address, BitConverter.GetBytes(value)); }
    finally { VirtualProtectEx(process, (nint)address, 4, old, out _); }
}

static void PatchCode(nint process, ulong address, byte[] bytes)
{
    if (!VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, PageExecuteReadWrite, out uint old)) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        WriteExact(process, address, bytes);
        FlushInstructionCache(process, (nint)address, (nuint)bytes.Length);
    }
    finally { VirtualProtectEx(process, (nint)address, (nuint)bytes.Length, old, out _); }
}

static byte[] ReadExact(nint process, ulong address, int count)
{
    byte[] bytes = new byte[count];
    if (!ReadProcessMemory(process, (nint)address, bytes, (nuint)count, out nuint read) || read != (nuint)count)
        throw new Win32Exception(Marshal.GetLastWin32Error());
    return bytes;
}

static bool TryReadExact(nint process, ulong address, int count, out byte[] bytes)
{
    bytes = new byte[count];
    return ReadProcessMemory(process, (nint)address, bytes, (nuint)count, out nuint read) && read == (nuint)count;
}

static float ReadFloat(nint process, ulong address) => BitConverter.ToSingle(ReadExact(process, address, 4));

static void WriteExact(nint process, ulong address, byte[] bytes)
{
    if (!WriteProcessMemory(process, (nint)address, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
        throw new Win32Exception(Marshal.GetLastWin32Error());
}

static bool SayNoToEacAppearsInstalled(string gameDirectory)
{
    string eac = Path.Combine(gameDirectory, "EasyAntiCheat");
    string x64 = Path.Combine(eac, "EasyAntiCheat_x64.dll"), x86 = Path.Combine(eac, "EasyAntiCheat_x86.dll");
    return Small(x64) && Small(x86) && Large(x64 + ".BAK") && Large(x86 + ".BAK");
    static bool Small(string path) => File.Exists(path) && new FileInfo(path).Length is > 0 and < 65536;
    static bool Large(string path) => File.Exists(path) && new FileInfo(path).Length > 262144;
}

static bool FirewallBlocksProgram(string program)
{
    string full = Path.GetFullPath(program);
    try
    {
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        foreach (dynamic rule in policy.Rules)
        {
            string? app = rule.ApplicationName as string;
            if (rule.Enabled && (int)rule.Direction == 2 && (int)rule.Action == 0 && !string.IsNullOrWhiteSpace(app) && string.Equals(Path.GetFullPath(app!), full, StringComparison.OrdinalIgnoreCase)) return true;
        }
    }
    catch { }
    return false;
}

[DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocationType, uint protect);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualProtectEx(nint process, nint address, nuint size, uint newProtect, out uint oldProtect);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool FlushInstructionCache(nint process, nint address, nuint size);
[DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
[DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
[DllImport("user32.dll")] static extern bool UnregisterHotKey(nint window, int id);
[DllImport("user32.dll")] static extern int GetMessage(out Msg message, nint window, uint min, uint max);
[DllImport("user32.dll")] static extern nint GetForegroundWindow();
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint processId);
[DllImport("user32.dll")] static extern short GetAsyncKeyState(int virtualKey);
[DllImport("user32.dll", SetLastError = true)] static extern nint SetWindowsHookEx(int hookId, LowLevelMouseProc callback, nint module, uint threadId);
[DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] static extern nint SetWindowsKeyboardHookEx(int hookId, LowLevelKeyboardProc callback, nint module, uint threadId);
[DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
[DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
[DllImport("user32.dll", SetLastError = true)] static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
[DllImport("user32.dll")] static extern bool KillTimer(nint window, nuint id);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? moduleName);
[DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
[DllImport("user32.dll", SetLastError = true)] static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

[StructLayout(LayoutKind.Sequential)] struct Msg { public nint HWnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; }
delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);
delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);
