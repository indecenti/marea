using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Storage.CloudFilters;

namespace Marea.CloudFiles;

/// <summary>Registration of a folder as a Cloud Files sync root owned by Marea.</summary>
public static class SyncRoot
{
    /// <summary>Stable provider id: identifies Marea to the cloud filter driver.</summary>
    public static readonly Guid ProviderId = new("5b1e3a8c-6d0f-4f3e-9a51-7a3c2e9d4b10");

    /// <summary>Registers <paramref name="root"/> as a sync root (idempotent: updates if present).</summary>
    /// <remarks>Policies: progressive hydration on first real read (a bare open never recalls data), fully populated folders, any change
    /// clears the in-sync state (so edited files can never be dehydrated by mistake).</remarks>
    /// <exception cref="CloudFilesException">When Windows refuses the registration.</exception>
    public static unsafe void Register(string root, string version)
    {
        fixed (char* pRoot = root, pName = ProviderName, pVer = version)
        {
            var reg = new CF_SYNC_REGISTRATION
            {
                StructSize = (uint)sizeof(CF_SYNC_REGISTRATION),
                ProviderName = pName,
                ProviderVersion = pVer,
                ProviderId = ProviderId,
            };
            var policies = new CF_SYNC_POLICIES
            {
                StructSize = (uint)sizeof(CF_SYNC_POLICIES),
                Hydration = new CF_HYDRATION_POLICY
                {
                    Primary = CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_PROGRESSIVE,
                    Modifier = CF_HYDRATION_POLICY_MODIFIER.CF_HYDRATION_POLICY_MODIFIER_ALLOW_FULL_RESTART_HYDRATION,
                },
                Population = new CF_POPULATION_POLICY
                {
                    Primary = CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_ALWAYS_FULL,
                },
                InSync = CF_INSYNC_POLICY.CF_INSYNC_POLICY_TRACK_ALL,
                HardLink = CF_HARDLINK_POLICY.CF_HARDLINK_POLICY_NONE,
                PlaceholderManagement = CF_PLACEHOLDER_MANAGEMENT_POLICY.CF_PLACEHOLDER_MANAGEMENT_POLICY_DEFAULT,
            };
            var flags = IsMareaRoot(root) ? CF_REGISTER_FLAGS.CF_REGISTER_FLAG_UPDATE : CF_REGISTER_FLAGS.CF_REGISTER_FLAG_NONE;
            flags |= CF_REGISTER_FLAGS.CF_REGISTER_FLAG_MARK_IN_SYNC_ON_ROOT
                   | CF_REGISTER_FLAGS.CF_REGISTER_FLAG_DISABLE_ON_DEMAND_POPULATION_ON_ROOT;
            CloudFilesException.ThrowIfFailed(PInvoke.CfRegisterSyncRoot(pRoot, &reg, &policies, flags),
                $"registrazione della cartella {root}");
        }
    }

    /// <summary>Unregisters the sync root. Call only after every file was hydrated and reverted.</summary>
    public static unsafe void Unregister(string root)
    {
        fixed (char* pRoot = root)
        {
            CloudFilesException.ThrowIfFailed(PInvoke.CfUnregisterSyncRoot(pRoot), $"rimozione della cartella {root}");
        }
    }

    /// <summary>Returns the provider name of the sync root containing <paramref name="path"/>, or null.</summary>
    public static unsafe string? ProviderNameOf(string path)
    {
        var info = new CF_SYNC_ROOT_PROVIDER_INFO();
        uint returned;
        fixed (char* p = path)
        {
            var hr = PInvoke.CfGetSyncRootInfoByPath(p, CF_SYNC_ROOT_INFO_CLASS.CF_SYNC_ROOT_INFO_PROVIDER,
                &info, (uint)sizeof(CF_SYNC_ROOT_PROVIDER_INFO), &returned);
            if (hr.Failed)
            {
                return null;
            }
        }
        return info.ProviderName.ToString();
    }

    /// <summary>Provider name written at registration time.</summary>
    public const string ProviderName = "Marea";

    /// <summary>True when <paramref name="path"/> lies inside a sync root registered by Marea.</summary>
    public static bool IsMareaRoot(string path) => ProviderNameOf(path) == ProviderName;

    /// <summary>Null when files under <paramref name="root"/> can be freed now, else a translation key
    /// (always null on Windows: the cloud filter fails reads cleanly while no provider is connected).</summary>
    public static string? NotReadyKey(string root) => null;

    /// <summary>True when <paramref name="path"/> lies inside a sync root of another provider (e.g. OneDrive).</summary>
    public static bool IsForeignRoot(string path)
    {
        var owner = ProviderNameOf(path);
        return owner is not null && owner != ProviderName;
    }
}

/// <summary>An HRESULT failure from the Cloud Files API, with a human readable context.</summary>
public sealed class CloudFilesException : Exception
{
    /// <summary>The failing HRESULT.</summary>
    public int HResultCode { get; }

    /// <summary>Creates the exception.</summary>
    public CloudFilesException(string message, int hr) : base(message) => HResultCode = hr;

    internal static void ThrowIfFailed(Windows.Win32.Foundation.HRESULT hr, string what)
    {
        if (hr.Failed)
        {
            var detail = Marshal.GetExceptionForHR(hr.Value)?.Message ?? "";
            throw new CloudFilesException($"Errore Cloud Files durante {what}: 0x{hr.Value:X8} {detail}", hr.Value);
        }
    }
}
