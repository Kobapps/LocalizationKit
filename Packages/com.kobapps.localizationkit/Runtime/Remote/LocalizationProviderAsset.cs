using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// A provider that lives in the project as an asset, configured in the inspector.
    /// </summary>
    /// <remarks>
    /// <see cref="ILocalizationProvider"/> is a plain interface and stays that way — a provider
    /// built in code, in a test, or from a config service has no business being a Unity object.
    /// But a provider almost always needs configuring, that configuration wants to be under version
    /// control next to the catalog, and the settings asset has to be able to reference one. All
    /// three of those want a <c>ScriptableObject</c>, so this is the base for one.
    /// <para>
    /// Derive, add serialized fields for whatever the remote needs, and implement the two verbs.
    /// The shipped Google Sheets sample is one of these and is about a hundred lines.
    /// </para>
    /// <para>
    /// <b>Keep write credentials out of players.</b> An asset referenced by anything in a build
    /// ships inside it, and a token in a build is a token you have published. A provider that
    /// uploads should either report no upload capability outside the editor — as the Sheets sample
    /// does — or read its credential from somewhere that is not the asset.
    /// </para>
    /// </remarks>
    public abstract class LocalizationProviderAsset : ScriptableObject, ILocalizationProvider
    {
        /// <inheritdoc />
        public virtual string DisplayName => name;

        /// <inheritdoc />
        public abstract LocalizationProviderCapabilities Capabilities { get; }

        /// <inheritdoc />
        public abstract void Fetch(Action<LocalizationFetchResult> onCompleted);

        /// <inheritdoc />
        public virtual void Upload(LocalizationSnapshot snapshot, Action<LocalizationUploadResult> onCompleted)
        {
            onCompleted?.Invoke(LocalizationUploadResult.Failed($"{DisplayName} cannot upload."));
        }
    }
}
