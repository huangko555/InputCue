using System.Runtime.InteropServices;
using InputCue.Windows.Interop;

namespace InputCue.Windows.InputContext;

internal sealed class ActiveInputProcessorProfileProbe
{
    internal static readonly ActiveInputProcessorProfileProbe Instance = new(
        WindowsActiveInputProcessorProfileNative.Instance);

    private readonly IActiveInputProcessorProfileNative _native;

    internal ActiveInputProcessorProfileProbe(IActiveInputProcessorProfileNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    internal InputProcessorProfileIdentity Observe() =>
        _native.TryGetActiveKeyboardProfile(out var profile)
            ? profile
            : default;
}

internal interface IActiveInputProcessorProfileNative
{
    public bool TryGetActiveKeyboardProfile(out InputProcessorProfileIdentity profile);
}

internal sealed class WindowsActiveInputProcessorProfileNative : IActiveInputProcessorProfileNative
{
    private const int GetActiveProfileVtableIndex = 10;
    private const uint InputProcessorProfileType = 1;

    private static readonly Guid ClsidTfInputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid IidTfInputProcessorProfileManager = new("71C6E74C-0F28-11D8-A82A-00065B84435C");
    private static readonly Guid KeyboardCategory = new("34745C63-B2F0-4784-8B67-5E12C8701A31");

    internal static readonly WindowsActiveInputProcessorProfileNative Instance = new();

    private WindowsActiveInputProcessorProfileNative()
    {
    }

    public bool TryGetActiveKeyboardProfile(out InputProcessorProfileIdentity profile)
    {
        profile = default;
        var initializeResult = NativeMethods.CoInitializeEx(0, NativeMethods.CoInitMultiThreaded);
        var shouldUninitialize = initializeResult is 0 or 1;
        try
        {
            if (initializeResult < 0 && initializeResult != NativeMethods.RpcChangedMode)
            {
                return false;
            }

            var createResult = NativeMethods.CoCreateInstance(
                ClsidTfInputProcessorProfiles,
                0,
                NativeMethods.ClassContextInprocServer,
                IidTfInputProcessorProfileManager,
                out var profileManager);
            if (createResult < 0 || profileManager == 0)
            {
                return false;
            }

            try
            {
                var vtable = Marshal.ReadIntPtr(profileManager);
                var getActiveProfileAddress = Marshal.ReadIntPtr(
                    vtable,
                    GetActiveProfileVtableIndex * IntPtr.Size);
                var getActiveProfile = Marshal.GetDelegateForFunctionPointer<GetActiveProfileDelegate>(
                    getActiveProfileAddress);
                var keyboardCategory = KeyboardCategory;
                var result = getActiveProfile(
                    profileManager,
                    ref keyboardCategory,
                    out var activeProfile);
                if (result != 0 ||
                    activeProfile.ProfileType != InputProcessorProfileType ||
                    activeProfile.ClassId == Guid.Empty ||
                    activeProfile.ProfileId == Guid.Empty)
                {
                    return false;
                }

                profile = new InputProcessorProfileIdentity(
                    activeProfile.ClassId,
                    activeProfile.ProfileId);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            finally
            {
                _ = Marshal.Release(profileManager);
            }
        }
        finally
        {
            if (shouldUninitialize)
            {
                NativeMethods.CoUninitialize();
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetActiveProfileDelegate(
        nint profileManager,
        ref Guid categoryId,
        out TfInputProcessorProfile profile);

    [StructLayout(LayoutKind.Sequential)]
    private struct TfInputProcessorProfile
    {
        internal uint ProfileType;
        internal ushort LanguageId;
        private ushort _padding;
        internal Guid ClassId;
        internal Guid ProfileId;
        internal Guid CategoryId;
        internal nint SubstituteLayout;
        internal uint Capabilities;
        internal nint KeyboardLayout;
        internal uint Flags;
    }
}
